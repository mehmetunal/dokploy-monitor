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

        // Kuyruk endpoint'i olmayan baglantilar bir kez tespit edilir, sonra atlanir.
        var unsupported = new HashSet<string>(StringComparer.Ordinal);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var clientFactory = scope.ServiceProvider.GetRequiredService<IDokployClientFactory>();
                var connections = await scope.ServiceProvider
                    .GetRequiredService<ConnectionService>()
                    .GetEnabledAsync(stoppingToken);

                var pending = connections.Where(c => !unsupported.Contains(c.Id)).ToList();
                if (pending.Count == 0)
                {
                    if (connections.Count > 0)
                    {
                        logger.LogInformation("Hicbir baglanti kuyruk endpoint'ini desteklemiyor; izleme durduruldu.");
                        return;
                    }

                    continue;
                }

                var totalJobs = 0;
                var fingerprints = new List<string>(pending.Count);

                foreach (var connection in pending)
                {
                    var snapshot = await clientFactory.Create(connection).GetQueueAsync(stoppingToken);
                    state.SetQueue(connection.Id, snapshot);

                    if (!snapshot.IsAvailable)
                    {
                        logger.LogInformation(
                            "'{Connection}' icin kuyruk izleme devre disi: {Reason}",
                            connection.Name,
                            snapshot.UnavailableReason);

                        unsupported.Add(connection.Id);
                        continue;
                    }

                    totalJobs += snapshot.Jobs.Count;
                    fingerprints.Add($"{connection.Id}:{string.Join('|', snapshot.Jobs.Select(j => $"{j.Id}:{j.State}"))}");
                }

                var fingerprint = string.Join(';', fingerprints);
                if (fingerprint == previousFingerprint)
                {
                    continue;
                }

                previousFingerprint = fingerprint;

                var sync = scope.ServiceProvider.GetRequiredService<DeploymentSyncService>();
                await sync.BroadcastAsync(stoppingToken);

                // Kuyruk hareketlendiyse deployment tablosu da birazdan degisecek.
                if (totalJobs > 0)
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
