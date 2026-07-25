namespace DokployMonitor.Core.Queueing;

/// <summary>
/// Dokploy kuyrugundaki bir is. Kaynak: `GET /api/deployment.queueList`.
/// Onemli: kuyrukta bekleyen (waiting) bir isin Dokploy tarafinda henuz
/// deployment kaydi olusmaz — "sirada kac tane var" sorusunun tek cevabi burasi.
/// </summary>
public sealed class QueueJob
{
    public required string Id { get; init; }

    /// <summary>waiting | active | delayed | completed | failed | cancelled | paused</summary>
    public required string State { get; init; }

    /// <summary>application | compose | application-preview</summary>
    public string? ApplicationType { get; init; }

    /// <summary>deploy | redeploy</summary>
    public string? JobType { get; init; }

    public string? ApplicationId { get; init; }
    public string? ComposeId { get; init; }
    public string? PreviewDeploymentId { get; init; }

    /// <summary>Dokploy'un deployment kaydina yazacagi baslik (titleLog).</summary>
    public string? Title { get; init; }

    public string? Description { get; init; }
    public string? ServerId { get; init; }

    /// <summary>Dokploy'un urettigi servis yolu (or. "Proje / Ortam / Servis").</summary>
    public string? ServicePath { get; init; }

    /// <summary>Isin kuyruga eklendigi an.</summary>
    public DateTimeOffset? EnqueuedAt { get; init; }

    /// <summary>Isin islenmeye baslandigi an (active olduysa).</summary>
    public DateTimeOffset? ProcessedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }
    public string? FailedReason { get; init; }

    public bool IsWaiting => string.Equals(State, "waiting", StringComparison.OrdinalIgnoreCase);
    public bool IsActive => string.Equals(State, "active", StringComparison.OrdinalIgnoreCase);

    /// <summary>Servis kimligi (uygulama veya compose).</summary>
    public string? ServiceId => ApplicationId ?? ComposeId;

    /// <summary>
    /// Dokploy kuyrugu is'leri sunucuya (partition) gore boler; ayni partition icinde
    /// ayni servise ait isler FIFO calisir. Bekleme sirasini hesaplarken bunu kullaniyoruz.
    /// </summary>
    public string Partition => ServerId ?? "__local__";
}

/// <summary>Kuyrugun bir anlik goruntusu (bellekte tutulur, kalici degil).</summary>
public sealed class QueueSnapshot
{
    public static readonly QueueSnapshot Empty = new()
    {
        Jobs = [],
        CapturedAt = DateTimeOffset.MinValue,
    };

    public required IReadOnlyList<QueueJob> Jobs { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>Kuyrugun okunamadigi durumlarda sebep (or. eski Dokploy surumu).</summary>
    public string? UnavailableReason { get; init; }

    public bool IsAvailable => UnavailableReason is null;

    public IEnumerable<QueueJob> Waiting => Jobs.Where(j => j.IsWaiting).OrderBy(j => j.EnqueuedAt);
    public IEnumerable<QueueJob> Active => Jobs.Where(j => j.IsActive).OrderBy(j => j.ProcessedAt ?? j.EnqueuedAt);

    /// <summary>
    /// Bekleyen isler icin 1'den baslayan sira numarasi uretir (partition bazinda FIFO).
    /// </summary>
    public IReadOnlyDictionary<string, int> WaitingPositions()
    {
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var partition in Waiting.GroupBy(j => j.Partition))
        {
            var index = 1;
            foreach (var job in partition.OrderBy(j => j.EnqueuedAt))
            {
                positions[job.Id] = index++;
            }
        }

        return positions;
    }
}
