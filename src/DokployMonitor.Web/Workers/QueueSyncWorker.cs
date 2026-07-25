using DokployMonitor.Core.Abstractions;
using DokployMonitor.Web.Options;
using DokployMonitor.Web.Services;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Workers;

/// <summary>
/// Dokploy kuyrugunu (`deployment.queueList`) izler.
///
/// Kuyrukta bekleyen bir is icin Dokploy henuz deployment kaydi olusturmaz; yani
/// "sirada ne var, kacinci sirada" sorusunun tek kaynagi bu endpoint. Kuyrukta
/// degisiklik gorulunce deployment senkronizasyonu da hemen tetiklenir, boylece
/// is calismaya basladigi anda panoya duser.
/// </summary>
public sealed class QueueSyncWorker(
    IServiceScopeFactory scopeFactory,
    MonitorState state,
    IOptions<MonitorOptions> options,
    ILogger<QueueSyncWorker> logger) : BackgroundService
{
    private readonly MonitorOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(2, _options.QueuePollSeconds));
        using var timer = new PeriodicTimer(interval);
        var previousFingerprint = string.Empty;

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dokploy = scope.ServiceProvider.GetRequiredService<IDokployClient>();

                var snapshot = await dokploy.GetQueueAsync(stoppingToken);
                state.Queue = snapshot;

                if (!snapshot.IsAvailable)
                {
                    // Endpoint yoksa istemci bunu hatirliyor; bosuna sorgulamaya devam etmeyelim.
                    logger.LogInformation("Kuyruk izleme devre disi: {Reason}", snapshot.UnavailableReason);
                    return;
                }

                var fingerprint = string.Join('|', snapshot.Jobs.Select(j => $"{j.Id}:{j.State}"));
                if (fingerprint == previousFingerprint)
                {
                    continue;
                }

                previousFingerprint = fingerprint;

                var sync = scope.ServiceProvider.GetRequiredService<DeploymentSyncService>();
                await sync.BroadcastAsync(stoppingToken);

                // Kuyruk hareketlendiyse deployment tablosu da birazdan degisecek.
                if (snapshot.Jobs.Count > 0)
                {
                    state.RequestSync(SyncTrigger.Webhook);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Kuyruk okunamadi.");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
