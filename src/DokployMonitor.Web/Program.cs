using DokployMonitor.Infrastructure;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Web.Hubs;
using DokployMonitor.Web.Options;
using DokployMonitor.Web.Services;
using DokployMonitor.Web.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection(MonitorOptions.SectionName));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection(WebhookOptions.SectionName));

builder.Services.AddDokployMonitorInfrastructure(builder.Configuration);

builder.Services.AddSingleton<MonitorState>();
builder.Services.AddScoped<DashboardQueryService>();
builder.Services.AddScoped<DeploymentSyncService>();

builder.Services.AddHostedService<DeploymentSyncWorker>();
builder.Services.AddHostedService<QueueSyncWorker>();
builder.Services.AddHostedService<RetentionWorker>();

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks().AddDbContextCheck<MonitorDbContext>();

var app = builder.Build();

await InitializeDatabaseAsync(app);

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

app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<DeploymentsHub>("/hubs/deployments");
app.MapHealthChecks("/health");

app.Run();

/// <summary>
/// Veritabanini hazirlar: klasoru olusturur, semayi uygular ve SQLite'i WAL moduna alir.
/// WAL sayesinde arka plan senkronizasyonu yazarken pano okumaya devam edebilir.
/// </summary>
static async Task InitializeDatabaseAsync(WebApplication app)
{
    var connectionString = app.Configuration.GetConnectionString("Default") ?? "Data Source=data/monitor.db";
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;

    if (Path.GetDirectoryName(Path.GetFullPath(dataSource)) is { Length: > 0 } directory)
    {
        Directory.CreateDirectory(directory);
    }

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
}
