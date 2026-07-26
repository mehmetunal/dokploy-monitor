using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.Dashboard;
using DokployMonitor.Core.Deployments;
using DokployMonitor.Infrastructure.Caching;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Services;

/// <summary>Panolarin ihtiyac duydugu sorgular. Yazma yapmaz.</summary>
public sealed class DashboardQueryService(
    MonitorDbContext db,
    MonitorState state,
    ConnectionService connections,
    CacheService cache,
    IOptions<Web.Options.MonitorOptions> options)
{
    private readonly Web.Options.MonitorOptions _options = options.Value;

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-24);

        // Calisanlar: en uzun suredir devam eden en ustte.
        var active = await db.Deployments
            .Where(d => d.Status == DeploymentStatus.Running)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

        var recent = await db.Deployments
            .OrderByDescending(d => d.CreatedAt)
            .Take(_options.RecentCount)
            .ToListAsync(ct);

        var succeeded = await db.Deployments
            .CountAsync(d => d.CreatedAt >= cutoff && d.Status == DeploymentStatus.Done, ct);

        var failed = await db.Deployments
            .CountAsync(d => d.CreatedAt >= cutoff
                && (d.Status == DeploymentStatus.Error || d.Status == DeploymentStatus.Cancelled), ct);

        var averageDuration = await db.Deployments
            .Where(d => d.CreatedAt >= cutoff && d.Status == DeploymentStatus.Done && d.DurationSeconds != null)
            .AverageAsync(d => (double?)d.DurationSeconds, ct);

        var connectionNames = await connections.GetNamesAsync(ct);

        // Kuyrukta bekleyen isler: Dokploy bunlar icin henuz deployment kaydi olusturmaz,
        // dolayisiyla "sirada ne var" bilgisinin tek kaynagi budur. Her baglantinin
        // kuyrugu ayri okunur, sira numaralari kendi kuyruguna gore hesaplanir.
        var queueRows = new List<QueueRow>();
        var queueProblems = new List<string>();

        foreach (var (connectionId, snapshot) in state.Queues)
        {
            var label = connectionNames.GetValueOrDefault(connectionId, connectionId);

            if (!snapshot.IsAvailable)
            {
                queueProblems.Add($"{label}: {snapshot.UnavailableReason}");
                continue;
            }

            var positions = snapshot.WaitingPositions();
            queueRows.AddRange(snapshot.Waiting.Select(job =>
                QueueRow.From(job, positions.GetValueOrDefault(job.Id), label)));
        }

        queueRows = [.. queueRows.OrderBy(row => row.Position ?? int.MaxValue).ThenBy(row => row.EnqueuedAt)];

        // Yalnizca hicbir kuyruk okunamadiginda gorunume uyari basilir.
        var queueUnavailableReason = queueRows.Count == 0 && queueProblems.Count > 0
            ? string.Join(" · ", queueProblems)
            : null;

        var notifications = await db.WebhookNotifications
            .OrderByDescending(n => n.ReceivedAt)
            .Take(10)
            .ToListAsync(ct);

        var longest = active.FirstOrDefault();

        string? Label(TrackedDeployment deployment) =>
            deployment.ConnectionId is { } id ? connectionNames.GetValueOrDefault(id, id) : null;

        return new DashboardSnapshot
        {
            Stats = new DashboardStats
            {
                RunningCount = active.Count,
                QueuedCount = queueRows.Count,
                SucceededLast24H = succeeded,
                FailedLast24H = failed,
                AverageDurationSecondsLast24H = averageDuration,
                LongestRunningElapsed = longest?.Elapsed(now),
                LongestRunningService = longest?.DisplayName,
                LastSyncAt = state.LastSyncAt,
                SyncError = state.LastSyncError,
            },
            Active = [.. active.Select(d => DeploymentRow.From(d, connectionName: Label(d)))],
            Recent = [.. recent.Select(d => DeploymentRow.From(d, connectionName: Label(d)))],
            Queue = queueRows,
            Notifications = [.. notifications.Select(NotificationRow.From)],
            QueueUnavailableReason = queueUnavailableReason,
        };
    }

    public Task<TrackedDeployment?> FindAsync(string deploymentId, CancellationToken ct = default) =>
        db.Deployments.FirstOrDefaultAsync(d => d.DeploymentId == deploymentId, ct);

    public Task<List<DeploymentEvent>> GetEventsAsync(string deploymentId, CancellationToken ct = default) =>
        db.DeploymentEvents
            .Where(e => e.DeploymentId == deploymentId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);

    /// <summary>Bir servisin son deployment gecmisi (servis detay ekrani).</summary>
    public Task<List<TrackedDeployment>> GetServiceHistoryAsync(string serviceId, int take, CancellationToken ct = default) =>
        db.Deployments
            .Where(d => d.ServiceId == serviceId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    /// <summary>Ayni projedeki diger servislerin son deploymentlari (detay ekrani).</summary>
    public Task<List<TrackedDeployment>> GetProjectHistoryAsync(
        string projectName,
        string? excludeServiceId,
        int take,
        CancellationToken ct = default) =>
        db.Deployments
            .Where(d => d.ProjectName == projectName)
            .Where(d => excludeServiceId == null || d.ServiceId != excludeServiceId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    /// <summary>
    /// En cok tekrar eden hatalar. Adet ve son gorulme, filtrelenmis kume uzerinden
    /// hesaplanir; hata metni ErrorSignature tablosundan alinir.
    /// </summary>
    public async Task<List<ErrorGroupRow>> GetTopErrorsAsync(
        ErrorFilter filter,
        int take,
        CancellationToken ct = default)
    {
        var failures = FailedDeployments(filter);

        var counts = await failures
            .GroupBy(d => d.ErrorSignatureHash!)
            .Select(group => new { Hash = group.Key, Count = group.Count() })
            .OrderByDescending(row => row.Count)
            .Take(take)
            .ToListAsync(ct);

        if (counts.Count == 0)
        {
            return [];
        }

        var hashes = counts.ConvertAll(row => row.Hash);

        var messages = await db.ErrorSignatures
            .Where(signature => hashes.Contains(signature.Hash))
            .ToDictionaryAsync(signature => signature.Hash, signature => signature.NormalizedMessage, ct);

        var rows = new List<ErrorGroupRow>(counts.Count);

        foreach (var count in counts)
        {
            // Hash basina tek kayit: sayi `take` ile sinirli (varsayilan 20), sorgular kucuk.
            var latest = await failures
                .Where(d => d.ErrorSignatureHash == count.Hash)
                .OrderByDescending(d => d.CreatedAt)
                .FirstOrDefaultAsync(ct);

            rows.Add(new ErrorGroupRow
            {
                Hash = count.Hash,
                Count = count.Count,
                NormalizedMessage = messages.GetValueOrDefault(count.Hash)
                    ?? DeploymentRow.Summarize(latest?.ErrorMessage)
                    ?? "(mesaj kaydedilmemis)",
                LastSeenAt = latest?.CreatedAt ?? DateTimeOffset.MinValue,
                LastServiceName = latest?.DisplayName,
                LastProjectName = latest?.ProjectName,
                LatestDeploymentId = latest?.DeploymentId,
                LatestHasLog = latest is not null
                    && (!string.IsNullOrWhiteSpace(latest.LogPath)
                        || !string.IsNullOrWhiteSpace(latest.ArchivedLogPath)),
            });
        }

        return rows;
    }

    /// <summary>Filtreye uyan son basarisiz deploymentlar (hata analizi ekrani).</summary>
    public Task<List<TrackedDeployment>> GetRecentFailuresAsync(
        ErrorFilter filter,
        int take,
        CancellationToken ct = default) =>
        FailedDeployments(filter, requireSignature: false)
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    /// <summary>
    /// Filtreli, sayfalanmis deployment listesi (gecmis ekrani). Toplam sayi da doner:
    /// pager'in sayfa sayisini hesaplamasi icin gerekiyor.
    /// </summary>
    public async Task<(List<TrackedDeployment> Rows, PageInfo Page)> SearchAsync(
        DeploymentFilter filter,
        CancellationToken ct = default)
    {
        var q = db.Deployments.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Project))
        {
            q = q.Where(d => d.ProjectName == filter.Project);
        }

        if (!string.IsNullOrWhiteSpace(filter.ConnectionId))
        {
            q = q.Where(d => d.ConnectionId == filter.ConnectionId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status)
            && Enum.TryParse<DeploymentStatus>(filter.Status, ignoreCase: true, out var parsed))
        {
            q = q.Where(d => d.Status == parsed);
        }

        if (filter.FromInstant is { } from)
        {
            q = q.Where(d => d.CreatedAt >= from);
        }

        if (filter.ToInstant is { } to)
        {
            q = q.Where(d => d.CreatedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var term = filter.Q.Trim();
            q = q.Where(d =>
                (d.ServiceName != null && EF.Functions.Like(d.ServiceName, $"%{term}%"))
                || (d.ProjectName != null && EF.Functions.Like(d.ProjectName, $"%{term}%"))
                || (d.ErrorMessage != null && EF.Functions.Like(d.ErrorMessage, $"%{term}%")));
        }

        var page = PageInfo.Create(filter.Page, filter.Size, await q.CountAsync(ct));

        var rows = await q.OrderByDescending(d => d.CreatedAt)
            .Skip(page.Skip)
            .Take(page.Size)
            .ToListAsync(ct);

        return (rows, page);
    }

    /// <summary>Hata analizi ekranlarinin ortak temel sorgusu.</summary>
    private IQueryable<TrackedDeployment> FailedDeployments(ErrorFilter filter, bool requireSignature = true)
    {
        var q = db.Deployments
            .Where(d => d.Status == DeploymentStatus.Error || d.Status == DeploymentStatus.Cancelled);

        if (requireSignature)
        {
            q = q.Where(d => d.ErrorSignatureHash != null);
        }

        if (!string.IsNullOrWhiteSpace(filter.Project))
        {
            q = q.Where(d => d.ProjectName == filter.Project);
        }

        if (filter.Since is { } since)
        {
            q = q.Where(d => d.CreatedAt >= since);
        }

        return q;
    }

    /// <summary>
    /// Filtre acilir kutulari icin proje adlari. Her ekran cizimde ayni sorgu
    /// tekrarlandigi icin onbelleklenir (bkz. CacheKeys.ProjectNames).
    /// </summary>
    public Task<List<string>> GetProjectNamesAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync(CacheKeys.ProjectNames, LoadProjectNamesAsync, ct: ct);

    private Task<List<string>> LoadProjectNamesAsync(CancellationToken ct) =>
        db.Deployments
            .Where(d => d.ProjectName != null)
            .Select(d => d.ProjectName!)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(ct);
}
