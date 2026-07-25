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

    private volatile QueueSnapshot _queue = QueueSnapshot.Empty;

    public QueueSnapshot Queue
    {
        get => _queue;
        set => _queue = value;
    }

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
