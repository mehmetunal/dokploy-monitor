using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using DokployMonitor.Core.Abstractions;
using DokployMonitor.Infrastructure.Dokploy;
using DokployMonitor.Infrastructure.Logs;
using DokployMonitor.Infrastructure.Persistence;
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
        services.AddOptions<DokployOptions>()
            .Bind(configuration.GetSection(DokployOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LogOptions>()
            .Bind(configuration.GetSection(LogOptions.SectionName));

        var dokployOptions = configuration.GetSection(DokployOptions.SectionName).Get<DokployOptions>() ?? new DokployOptions();
        var attemptTimeout = TimeSpan.FromSeconds(Math.Clamp(dokployOptions.TimeoutSeconds, 5, 120));

        services.AddHttpClient<IDokployClient, DokployApiClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<DokployOptions>>().Value;
                client.BaseAddress = options.ApiBaseUri();
                client.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // Zaman asimini direnc katmani yonetiyor; HttpClient'in kendi timeout'u devre disi.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<DokployOptions>>().Value;
                var handler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    AutomaticDecompression = DecompressionMethods.All,
                };

                if (options.AllowInvalidCertificates)
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

        var connectionString = configuration.GetConnectionString("Default")
            ?? "Data Source=data/monitor.db";

        services.AddDbContextFactory<MonitorDbContext>(options => options.UseSqlite(connectionString));

        // Controller/servisler icin scoped DbContext; worker'lar factory'yi dogrudan kullanir.
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<MonitorDbContext>>().CreateDbContext());

        services.AddSingleton<IDeploymentLogReader, FileDeploymentLogReader>();

        return services;
    }
}
