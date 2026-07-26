using System.Collections.Concurrent;
using System.Threading.Channels;
using DokployMonitor.Core.Queueing;

namespace DokployMonitor.Web.Services;

/// <summary>
/// Uygulama genelinde paylasilan anlik durum: son kuyruk goruntusu, son
/// senkronizasyon zamani/hatasi ve "hemen senkronize et" tetikleyicisi.
/// Kuyruk kalici olarak saklanmaz — anlik bir goruntudur.
/// </summary>
public sealed class MonitorState
{
    private readonly Channel<SyncTrigger> _triggers = Channel.CreateBounded<SyncTrigger>(
        new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    // Kuyruk baglanti basina tutulur: her Dokploy sunucusunun kendi kuyrugu var.
    private readonly ConcurrentDictionary<string, QueueSnapshot> _queues = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, QueueSnapshot> Queues => _queues;

    public void SetQueue(string connectionId, QueueSnapshot snapshot) => _queues[connectionId] = snapshot;

    /// <summary>Baglanti silindiginde/kapatildiginda kuyruk goruntusunu dusur.</summary>
    public void ForgetQueue(string connectionId) => _queues.TryRemove(connectionId, out _);

    public DateTimeOffset? LastSyncAt { get; set; }
    public string? LastSyncError { get; set; }
    public bool HasActiveDeployments { get; set; }

    /// <summary>
    /// Beklemeden senkronizasyon istenir (webhook geldiginde veya kullanici
    /// redeploy tetikledikten sonra). Kanal doluysa istek sessizce dusurulur —
    /// zaten cok yakinda bir senkronizasyon calisacak demektir.
    /// </summary>
    public void RequestSync(SyncTrigger trigger) => _triggers.Writer.TryWrite(trigger);

    /// <summary>
    /// Ya bir tetikleyici gelene ya da <paramref name="timeout"/> dolana kadar bekler.
    /// Tetikleyici geldiyse true doner (senkronizasyon hemen calisir).
    /// </summary>
    public async Task<bool> WaitForTriggerAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await _triggers.Reader.ReadAsync(timeoutSource.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

public enum SyncTrigger
{
    Webhook = 1,
    UserAction = 2,
}
