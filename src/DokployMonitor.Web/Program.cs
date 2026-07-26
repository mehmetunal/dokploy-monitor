using DokployMonitor.Infrastructure;
using DokployMonitor.Infrastructure.Identity;
using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Infrastructure.Validation;
using DokployMonitor.Web.Filters;
using DokployMonitor.Web.Hubs;
using DokployMonitor.Web.Options;
using DokployMonitor.Web.Services;
using DokployMonitor.Web.Workers;
using FluentMigrator.Runner;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Web katmanindaki validator'lar (options + istek modelleri). Options dogrulamasi kok
// kapsamdan cozuldugu icin singleton kaydediliyor.
builder.Services.AddValidatorsFromAssemblyContaining<MonitorOptionsValidator>(ServiceLifetime.Singleton);

builder.Services.AddOptions<MonitorOptions>()
    .Bind(builder.Configuration.GetSection(MonitorOptions.SectionName))
    .ValidateWithFluentValidation()
    .ValidateOnStart();

builder.Services.AddOptions<WebhookOptions>()
    .Bind(builder.Configuration.GetSection(WebhookOptions.SectionName))
    .ValidateWithFluentValidation()
    .ValidateOnStart();

builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateWithFluentValidation()
    .ValidateOnStart();

builder.Services.AddDokployMonitorInfrastructure(builder.Configuration);

// --------------------------------------------------------------- Kimlik dogrulama
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = Math.Clamp(authOptions.MinimumPasswordLength, 8, 64);
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 4;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddEntityFrameworkStores<MonitorDbContext>()
    .AddClaimsPrincipalFactory<MonitorClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(Math.Clamp(authOptions.SessionDays, 1, 365));
    options.SlidingExpiration = true;
    options.Cookie.Name = "trimango-dokploy-monitor.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Tum uc noktalar varsayilan olarak giris ister; istisnalar [AllowAnonymous] ile isaretli.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddSingleton<MonitorState>();
builder.Services.AddScoped<ConnectionService>();
builder.Services.AddScoped<DashboardQueryService>();
builder.Services.AddScoped<DeploymentSyncService>();

builder.Services.AddHostedService<DeploymentSyncWorker>();
builder.Services.AddHostedService<QueueSyncWorker>();
builder.Services.AddHostedService<RetentionWorker>();
builder.Services.AddHostedService<TranslationRefreshWorker>();

builder.Services.AddControllersWithViews(options =>
{
    // Varsayilan kimlik bilgileri degistirilmeden panelin hicbir yeri kullanilamaz.
    options.Filters.Add<RequireCredentialChangeFilter>();
})
    // Ceviriler veritabanindan gelir (bkz. DatabaseStringLocalizerFactory).
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks().AddDbContextCheck<MonitorDbContext>();

var app = builder.Build();

await InitializeDatabaseAsync(app);

await using (var seedScope = app.Services.CreateAsyncScope())
{
    // Kayit ekrani yok: ilk yonetici hesabi burada olusur (varsa dokunulmaz).
    await IdentitySeeder.SeedAsync(seedScope.ServiceProvider);

    // Ortam degiskenlerindeki tek baglanti varsa veritabanina tasi (geriye uyumluluk).
    await seedScope.ServiceProvider.GetRequiredService<ConnectionService>().ImportFromConfigurationAsync();

    // Ceviriler: eksik olanlar eklenir, mevcut kayitlar korunur; sonra bellege yuklenir.
    await seedScope.ServiceProvider.GetRequiredService<TranslationStore>().SeedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Dashboard/Error");
    app.UseHsts();
}

// Not: HTTPS yonlendirmesi bilerek yok — TLS'i Dokploy'un onundeki Traefik sonlandiriyor.
app.UseSerilogRequestLogging(options =>
{
    // Pano birkac saniyede bir veri cekebiliyor; log gurultusunu azalt.
    options.GetLevel = (httpContext, _, exception) =>
        exception is not null ? Serilog.Events.LogEventLevel.Error
        : httpContext.Request.Path.StartsWithSegments("/hubs") ? Serilog.Events.LogEventLevel.Verbose
        : httpContext.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
        : Serilog.Events.LogEventLevel.Information;
});

// Dil secimi: cerez (kullanicinin secimi) → Accept-Language (sistem dili) → varsayilan.
app.UseRequestLocalization(LocalizationSetup.Build());

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// CSS/JS giris ekraninda da gerekli: statik varliklar giris istemez.
app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<DeploymentsHub>("/hubs/deployments");

// Saglik ucu izleme sistemleri tarafindan cagrilir; kimlik dogrulamadan muaf.
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

/// <summary>
/// Veritabanini hazirlar: klasoru olusturur, FluentMigrator ile semayi uygular ve
/// SQLite'i WAL moduna alir. WAL sayesinde arka plan senkronizasyonu yazarken pano
/// okumaya devam edebilir.
/// </summary>
static async Task InitializeDatabaseAsync(WebApplication app)
{
    var connectionString = app.Configuration.GetConnectionString("Default") ?? "Data Source=data/monitor.db";
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;

    // FluentMigrator kendi baglantisini acar; dosyanin klasoru onceden var olmali.
    if (Path.GetDirectoryName(Path.GetFullPath(dataSource)) is { Length: > 0 } directory)
    {
        Directory.CreateDirectory(directory);
    }

    await using var scope = app.Services.CreateAsyncScope();

    scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();

    var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
}
