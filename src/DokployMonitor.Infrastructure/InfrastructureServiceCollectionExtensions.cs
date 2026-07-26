using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using DokployMonitor.Core.Abstractions;
using DokployMonitor.Infrastructure.Caching;
using DokployMonitor.Infrastructure.Docker;
using DokployMonitor.Infrastructure.Dokploy;
using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Infrastructure.Logs;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Infrastructure.Persistence.Migrations;
using DokployMonitor.Infrastructure.Validation;
using FluentMigrator.Runner;
using FluentValidation;
using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace DokployMonitor.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddDokployMonitorInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Validator'lar singleton: options dogrulamasi kok kapsamdan cozuluyor.
        services.AddValidatorsFromAssemblyContaining<DokployOptionsValidator>(ServiceLifetime.Singleton);

        services.AddOptions<DokployOptions>()
            .Bind(configuration.GetSection(DokployOptions.SectionName))
            .ValidateWithFluentValidation()
            .ValidateOnStart();

        services.AddOptions<LogOptions>()
            .Bind(configuration.GetSection(LogOptions.SectionName))
            .ValidateWithFluentValidation()
            .ValidateOnStart();

        services.AddOptions<DockerOptions>()
            .Bind(configuration.GetSection(DockerOptions.SectionName))
            .ValidateWithFluentValidation()
            .ValidateOnStart();

        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .ValidateWithFluentValidation()
            .ValidateOnStart();

        // Onbellek: Redis secilmisse dagitik, aksi halde bellek ici. Cagri yerleri
        // her iki durumda da IDistributedCache/CacheService kullanir.
        var cacheOptions = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new CacheOptions();

        if (cacheOptions.UsesRedis)
        {
            services.AddStackExchangeRedisCache(redis =>
            {
                redis.Configuration = cacheOptions.RedisConnectionString;
                redis.InstanceName = cacheOptions.InstanceName;
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<CacheService>();

        var dokployOptions = configuration.GetSection(DokployOptions.SectionName).Get<DokployOptions>() ?? new DokployOptions();
        var attemptTimeout = TimeSpan.FromSeconds(Math.Clamp(dokployOptions.TimeoutSeconds, 5, 120));

        // Adres ve API anahtari baglanti basina degistigi icin adlandirilmis istemciler
        // yalnizca handler/direnc politikasini tasir; geri kalanini fabrika doldurur.
        // Sertifika dogrulamasi handler seviyesinde oldugundan iki varyant var.
        foreach (var (name, allowInvalidCertificates) in new[]
                 {
                     (DokployClientFactory.ClientName, false),
                     (DokployClientFactory.InsecureClientName, true),
                 })
        {
            services.AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    AutomaticDecompression = DecompressionMethods.All,
                };

                if (allowInvalidCertificates)
                {
                    handler.SslOptions.RemoteCertificateValidationCallback =
                        (_, _, _, _) => true;
                }

                return handler;
            })
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = attemptTimeout;
                o.TotalRequestTimeout.Timeout = attemptTimeout * 3;
                o.CircuitBreaker.SamplingDuration = attemptTimeout * 2;
                o.Retry.MaxRetryAttempts = 2;

                // POST'lar (redeploy/kill) yeniden denenmez: tekrar gonderim
                // istemeden ikinci bir deployment kuyruga eklenmesine yol acabilir.
                o.Retry.ShouldHandle = args =>
                {
                    if (args.Outcome.Result is { } response)
                    {
                        if (response.RequestMessage?.Method == HttpMethod.Post)
                        {
                            return ValueTask.FromResult(false);
                        }

                        return ValueTask.FromResult(
                            (int)response.StatusCode >= 500
                            || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests);
                    }

                    return ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or TimeoutRejectedException);
                };
            });
        }

        services.AddSingleton<IDokployClientFactory, DokployClientFactory>();

        var connectionString = configuration.GetConnectionString("Default")
            ?? "Data Source=data/monitor.db";

        // Sema FluentMigrator ile yonetiliyor; EF Core yalnizca sorgu/kayit katmani.
        // Iki taraf ayni tablolari kullandigi icin sema testi zorunlu: bkz. MigrationSchemaTests.
        services.AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InitialSchema).Assembly).For.Migrations())
            .AddLogging(logging => logging.AddFluentMigratorConsole());

        services.AddDbContextFactory<MonitorDbContext>(options => options.UseSqlite(connectionString));

        // Controller/servisler icin scoped DbContext; worker'lar factory'yi dogrudan kullanir.
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<MonitorDbContext>>().CreateDbContext());

        services.AddSingleton<IDeploymentLogReader, FileDeploymentLogReader>();

        // Container loglari (docker logs karsiligi) Engine API'sinden unix soketi uzerinden okunur.
        services.AddHttpClient(DockerLogReader.HttpClientName, client =>
            {
                // Unix soketinde host adi anlamsiz; yalnizca yol kismi kullanilir.
                client.BaseAddress = new Uri("http://docker/");
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var dockerOptions = sp.GetRequiredService<IOptions<DockerOptions>>().Value;

                return new SocketsHttpHandler
                {
                    ConnectCallback = async (_, cancellationToken) =>
                    {
                        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                        await socket.ConnectAsync(
                            new UnixDomainSocketEndPoint(dockerOptions.SocketPath),
                            cancellationToken);

                        return new NetworkStream(socket, ownsSocket: true);
                    },
                };
            });

        services.AddSingleton<IContainerLogReader, DockerLogReader>();

        // Ceviriler veritabanindan gelir (resx yok): anlik goruntu singleton'da tutulur,
        // localizer bunun uzerinden senkron okur.
        services.AddSingleton<TranslationStore>();
        services.AddSingleton<IStringLocalizerFactory, DatabaseStringLocalizerFactory>();
        services.AddSingleton<IStringLocalizer, DatabaseStringLocalizer>();
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(DatabaseStringLocalizer<>));

        return services;
    }
}
