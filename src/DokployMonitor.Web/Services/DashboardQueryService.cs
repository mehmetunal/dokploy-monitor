using DokployMonitor.Core.Dashboard;
using DokployMonitor.Core.Deployments;
using DokployMonitor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Services;

/// <summary>Panolarin ihtiyac duydugu sorgular. Yazma yapmaz.</summary>
public sealed class DashboardQueryService(
    MonitorDbContext db,
    MonitorState state,
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

        var queue = state.Queue;
        var positions = queue.WaitingPositions();

        // Kuyrukta bekleyen isler: Dokploy bunlar icin henuz deployment kaydi olusturmaz,
        // dolayisiyla "sirada ne var" bilgisinin tek kaynagi budur.
        var queueRows = queue.Waiting
            .Select(job => QueueRow.From(job, positions.GetValueOrDefault(job.Id)))
            .ToList();

        var notifications = await db.WebhookNotifications
            .OrderByDescending(n => n.ReceivedAt)
            .Take(10)
            .ToListAsync(ct);

        var longest = active.FirstOrDefault();

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
            Active = [.. active.Select(d => DeploymentRow.From(d))],
            Recent = [.. recent.Select(d => DeploymentRow.From(d))],
            Queue = queueRows,
            Notifications = [.. notifications.Select(NotificationRow.From)],
            QueueUnavailableReason = queue.UnavailableReason,
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

    /// <summary>En cok tekrar eden hatalar (hata analizi ekrani).</summary>
    public Task<List<ErrorSignature>> GetTopErrorsAsync(int take, CancellationToken ct = default) =>
        db.ErrorSignatures
            .OrderByDescending(s => s.OccurrenceCount)
            .ThenByDescending(s => s.LastSeenAt)
            .Take(take)
            .ToListAsync(ct);

    /// <summary>Filtreli deployment listesi (gecmis ekrani).</summary>
    public async Task<List<TrackedDeployment>> SearchAsync(
        string? projectName,
        string? status,
        string? query,
        int take,
        CancellationToken ct = default)
    {
        var q = db.Deployments.AsQueryable();

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            q = q.Where(d => d.ProjectName == projectName);
        }

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<DeploymentStatus>(status, ignoreCase: true, out var parsed))
        {
            q = q.Where(d => d.Status == parsed);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(d =>
                (d.ServiceName != null && EF.Functions.Like(d.ServiceName, $"%{term}%"))
                || (d.ProjectName != null && EF.Functions.Like(d.ProjectName, $"%{term}%"))
                || (d.ErrorMessage != null && EF.Functions.Like(d.ErrorMessage, $"%{term}%")));
        }

        return await q.OrderByDescending(d => d.CreatedAt).Take(take).ToListAsync(ct);
    }

    /// <summary>Filtre acilir kutulari icin proje adlari.</summary>
    public Task<List<string>> GetProjectNamesAsync(CancellationToken ct = default) =>
        db.Deployments
            .Where(d => d.ProjectName != null)
            .Select(d => d.ProjectName!)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(ct);
}
