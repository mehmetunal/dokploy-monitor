using DokployMonitor.Core.Deployments;
using DokployMonitor.Core.Notifications;
using DokployMonitor.Core.Queueing;

namespace DokployMonitor.Core.Dashboard;

/// <summary>Panonun ust seridindeki ozet gostergeler.</summary>
public sealed record DashboardStats
{
    public int RunningCount { get; init; }
    public int QueuedCount { get; init; }
    public int SucceededLast24H { get; init; }
    public int FailedLast24H { get; init; }
    public double? AverageDurationSecondsLast24H { get; init; }
    public TimeSpan? LongestRunningElapsed { get; init; }
    public string? LongestRunningService { get; init; }
    public DateTimeOffset? LastSyncAt { get; init; }
    public string? SyncError { get; init; }
}

/// <summary>Tabloda gosterilen tek bir deployment satiri.</summary>
public sealed record DeploymentRow
{
    public required string DeploymentId { get; init; }
    public required string Status { get; init; }
    public required string ServiceName { get; init; }
    public string? ProjectName { get; init; }
    public string? EnvironmentName { get; init; }
    public required string ServiceType { get; init; }
    public string? Title { get; init; }
    public string? ErrorSummary { get; init; }
    public string? ServerName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public int? DurationSeconds { get; init; }
    public bool IsPreview { get; init; }
    public bool HasLog { get; init; }

    /// <summary>Kaydin geldigi Dokploy baglantisinin adi (coklu sunucu kurulumlarinda).</summary>
    public string? ConnectionName { get; init; }

    /// <summary>Kuyrukta bekleyen isler icin sira numarasi; calisanlarda null.</summary>
    public int? QueuePosition { get; init; }

    public static DeploymentRow From(
        TrackedDeployment d,
        int? queuePosition = null,
        string? connectionName = null) => new()
    {
        DeploymentId = d.DeploymentId,
        Status = d.Status.ToString().ToLowerInvariant(),
        ServiceName = d.DisplayName,
        ProjectName = d.ProjectName,
        EnvironmentName = d.EnvironmentName,
        ServiceType = d.ServiceType,
        Title = d.Title,
        ErrorSummary = Summarize(d.ErrorMessage),
        ServerName = d.ServerName,
        CreatedAt = d.CreatedAt,
        StartedAt = d.StartedAt,
        FinishedAt = d.FinishedAt,
        DurationSeconds = d.DurationSeconds,
        IsPreview = d.IsPreviewDeployment,
        HasLog = !string.IsNullOrWhiteSpace(d.LogPath) || !string.IsNullOrWhiteSpace(d.ArchivedLogPath),
        QueuePosition = queuePosition,
        ConnectionName = connectionName,
    };

    /// <summary>Hata mesajinin tabloya sigacak ilk anlamli satiri.</summary>
    public static string? Summarize(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return null;
        }

        var line = errorMessage
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.Length > 0) ?? errorMessage.Trim();

        return line.Length <= 160 ? line : string.Concat(line.AsSpan(0, 157), "...");
    }
}

/// <summary>Kuyrukta bekleyen is satiri (henuz deployment kaydi yok).</summary>
public sealed record QueueRow
{
    public required string JobId { get; init; }
    public required string State { get; init; }
    public required string ServiceLabel { get; init; }
    public string? JobType { get; init; }
    public string? ApplicationType { get; init; }
    public DateTimeOffset? EnqueuedAt { get; init; }
    public int? Position { get; init; }
    public string? ServiceId { get; init; }

    /// <summary>Isin bekledigi Dokploy baglantisinin adi.</summary>
    public string? ConnectionName { get; init; }

    public static QueueRow From(QueueJob job, int? position, string? connectionName = null) => new()
    {
        JobId = job.Id,
        State = job.State,
        ServiceLabel = job.ServicePath ?? job.Title ?? job.ServiceId ?? job.Id,
        JobType = job.JobType,
        ApplicationType = job.ApplicationType,
        EnqueuedAt = job.EnqueuedAt,
        Position = position,
        ServiceId = job.ServiceId,
        ConnectionName = connectionName,
    };
}

/// <summary>Dokploy webhook'undan gelen anlik bildirim satiri.</summary>
public sealed record NotificationRow
{
    public required string Title { get; init; }
    public string? Status { get; init; }
    public string? ProjectName { get; init; }
    public string? ApplicationName { get; init; }
    public string? ErrorMessage { get; init; }
    public string? BuildLink { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }

    public static NotificationRow From(WebhookNotification n) => new()
    {
        Title = n.Title ?? "Bildirim",
        Status = n.Status,
        ProjectName = n.ProjectName,
        ApplicationName = n.ApplicationName,
        ErrorMessage = DeploymentRow.Summarize(n.ErrorMessage),
        BuildLink = n.BuildLink,
        ReceivedAt = n.ReceivedAt,
    };
}

/// <summary>
/// Hata analizi ekranindaki tek bir hata grubu. Adet ve son gorulme, ekrandaki
/// filtreye (proje / tarih araligi) gore hesaplanir — bu yuzden ErrorSignature
/// varliginin global sayaclari yerine bu satir kullanilir.
/// </summary>
public sealed record ErrorGroupRow
{
    public required string Hash { get; init; }
    public required string NormalizedMessage { get; init; }
    public int Count { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
    public string? LastServiceName { get; init; }
    public string? LastProjectName { get; init; }

    /// <summary>Bu hatayi en son veren deployment; log onizlemesi buradan acilir.</summary>
    public string? LatestDeploymentId { get; init; }
    public bool LatestHasLog { get; init; }
}

/// <summary>Ana panonun tek seferde ihtiyac duydugu her sey.</summary>
public sealed record DashboardSnapshot
{
    public required DashboardStats Stats { get; init; }
    public required IReadOnlyList<DeploymentRow> Active { get; init; }
    public required IReadOnlyList<DeploymentRow> Recent { get; init; }
    public required IReadOnlyList<QueueRow> Queue { get; init; }
    public required IReadOnlyList<NotificationRow> Notifications { get; init; }
    public string? QueueUnavailableReason { get; init; }
}
