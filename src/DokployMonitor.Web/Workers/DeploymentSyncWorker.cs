using DokployMonitor.Web.Options;
using DokployMonitor.Web.Services;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Workers;

/// <summary>
/// Deployment senkronizasyonunu yuruten arka plan servisi.
///
/// Aralik uyarlanabilir: hicbir sey calismiyorken seyrek (varsayilan 15 sn),
/// aktif bir deployment varken sik (2 sn) sorgular. Webhook veya panelden
/// tetiklenen bir aksiyon oldugunda beklemeden hemen senkronize eder.
/// </summary>
public sealed class DeploymentSyncWorker(
    IServiceScopeFactory scopeFactory,
    MonitorState state,
    IOptions<MonitorOptions> options,
    ILogger<DeploymentSyncWorker> logger) : BackgroundService
{
    private readonly MonitorOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Deployment senkronizasyonu basliyor.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await RunOnceAsync(stoppingToken);

            var interval = TimeSpan.FromSeconds(result.Active > 0
                ? Math.Max(1, _options.ActivePollSeconds)
                : Math.Max(2, _options.IdlePollSeconds));

            // Hata durumunda geri cekil: Dokploy kapali olabilir, bosuna yuklenme.
            if (result.Failed)
            {
                interval = TimeSpan.FromSeconds(Math.Max(10, _options.IdlePollSeconds * 2));
            }

            var triggered = await state.WaitForTriggerAsync(interval, stoppingToken);
            if (triggered)
            {
                logger.LogDebug("Senkronizasyon disaridan tetiklendi.");
            }
        }
    }

    private async Task<SyncResult> RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sync = scope.ServiceProvider.GetRequiredService<DeploymentSyncService>();
            return await sync.SyncAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new SyncResult(0, 0, false, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Senkronizasyon dongusunde beklenmeyen hata.");
            return new SyncResult(0, 0, true, ex.Message);
        }
    }
}
