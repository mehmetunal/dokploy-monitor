using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.Deployments;
using DokployMonitor.Infrastructure.Logs;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Services;

public sealed record SyncResult(int Changed, int Active, bool Failed, string? Error);

/// <summary>
/// Dokploy'daki deployment durumunu bizim veritabanimizla eslestirir, degisiklikler
/// icin olay kaydi uretir ve pano abonelerine canli guncelleme gonderir.
/// </summary>
public sealed class DeploymentSyncService(
    IDokployClient dokploy,
    MonitorDbContext db,
    IDeploymentLogReader logReader,
    DashboardQueryService dashboard,
    IHubContext<DeploymentsHub> hub,
    MonitorState state,
    IOptions<Web.Options.MonitorOptions> monitorOptions,
    IOptions<LogOptions> logOptions,
    ILogger<DeploymentSyncService> logger)
{
    /// <summary>SQLite parametre sinirina takilmamak icin IN sorgularini parcala.</summary>
    private const int LookupChunkSize = 400;

    private readonly Web.Options.MonitorOptions _options = monitorOptions.Value;
    private readonly LogOptions _logOptions = logOptions.Value;

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        IReadOnlyList<TrackedDeployment> incoming;
        try
        {
            incoming = await dokploy.GetAllDeploymentsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Dokploy'dan deployment listesi alinamadi.");
            state.LastSyncError = ex.Message;
            await BroadcastAsync(ct);
            return new SyncResult(0, 0, true, ex.Message);
        }

        var now = DateTimeOffset.UtcNow;
        var isFirstRun = !await db.Deployments.AnyAsync(ct);
        var freshWindow = now.AddMinutes(-Math.Abs(_options.FreshFinishWindowMinutes));

        var existing = await LoadExistingAsync(incoming.Select(d => d.DeploymentId).ToList(), ct);
        var changed = 0;

        foreach (var candidate in incoming)
        {
            if (!existing.TryGetValue(candidate.DeploymentId, out var stored))
            {
                changed += await InsertAsync(candidate, now, isFirstRun, freshWindow, ct) ? 1 : 0;
                continue;
            }

            changed += await UpdateAsync(stored, candidate, now, ct) ? 1 : 0;
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        state.LastSyncAt = now;
        state.LastSyncError = null;

        var activeCount = incoming.Count(d => d.Status.IsActive());
        state.HasActiveDeployments = activeCount > 0;

        if (changed > 0)
        {
            await BroadcastAsync(ct);
        }

        return new SyncResult(changed, activeCount, false, null);
    }

    /// <summary>Panonun anlik goruntusunu tum baglı istemcilere gonderir.</summary>
    public async Task BroadcastAsync(CancellationToken ct = default)
    {
        var snapshot = await dashboard.GetSnapshotAsync(ct);
        await hub.Clients.All.SendAsync("dashboard", snapshot, ct);
    }

    private async Task<Dictionary<string, TrackedDeployment>> LoadExistingAsync(
        List<string> ids,
        CancellationToken ct)
    {
        var result = new Dictionary<string, TrackedDeployment>(StringComparer.Ordinal);

        foreach (var chunk in ids.Chunk(LookupChunkSize))
        {
            var batch = await db.Deployments
                .Where(d => chunk.Contains(d.DeploymentId))
                .ToListAsync(ct);

            foreach (var item in batch)
            {
                result[item.DeploymentId] = item;
            }
        }

        return result;
    }

    private async Task<bool> InsertAsync(
        TrackedDeployment candidate,
        DateTimeOffset now,
        bool isFirstRun,
        DateTimeOffset freshWindow,
        CancellationToken ct)
    {
        candidate.FirstSeenAt = now;
        candidate.LastUpdatedAt = now;

        if (candidate.Status.IsFailure())
        {
            await ApplyFailureAsync(candidate, now, ct);
        }

        db.Deployments.Add(candidate);

        // Ilk calistirmada gecmis kayitlar toplu yuklenir; bunlar icin olay uretmeyiz.
        if (isFirstRun)
        {
            return true;
        }

        if (candidate.Status.IsActive())
        {
            AddEvent(candidate.DeploymentId, DeploymentEventType.Started, null, candidate.Status, now, candidate.Title);
        }
        else if ((candidate.FinishedAt ?? candidate.CreatedAt) >= freshWindow)
        {
            // Cok kisa suren deployment'lari iki senkron arasinda kacirmis olabiliriz;
            // yakin zamanda bittiyse yine de "sonuclandi" olarak isaretle.
            AddEvent(candidate.DeploymentId, DeploymentEventType.Finished, null, candidate.Status, now, candidate.ErrorMessage ?? candidate.Title);
        }

        return true;
    }

    private async Task<bool> UpdateAsync(
        TrackedDeployment stored,
        TrackedDeployment candidate,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var statusChanged = stored.Status != candidate.Status;
        var hasChanges = statusChanged
            || stored.FinishedAt != candidate.FinishedAt
            || stored.StartedAt != candidate.StartedAt
            || stored.ErrorMessage != candidate.ErrorMessage
            || stored.Pid != candidate.Pid
            || stored.LogPath != candidate.LogPath
            || stored.ServiceName != candidate.ServiceName
            || stored.ProjectName != candidate.ProjectName;

        if (!hasChanges)
        {
            return false;
        }

        var previousStatus = stored.Status;

        stored.Status = candidate.Status;
        stored.StartedAt = candidate.StartedAt;
        stored.FinishedAt = candidate.FinishedAt;
        stored.DurationSeconds = candidate.DurationSeconds;
        stored.ErrorMessage = candidate.ErrorMessage;
        stored.Pid = candidate.Pid;
        stored.LogPath = candidate.LogPath;
        stored.Title = candidate.Title ?? stored.Title;
        stored.Description = candidate.Description ?? stored.Description;
        stored.ServiceName = candidate.ServiceName ?? stored.ServiceName;
        stored.AppName = candidate.AppName ?? stored.AppName;
        stored.ProjectId = candidate.ProjectId ?? stored.ProjectId;
        stored.ProjectName = candidate.ProjectName ?? stored.ProjectName;
        stored.EnvironmentId = candidate.EnvironmentId ?? stored.EnvironmentId;
        stored.EnvironmentName = candidate.EnvironmentName ?? stored.EnvironmentName;
        stored.ServerName = candidate.ServerName ?? stored.ServerName;
        stored.BuildServerName = candidate.BuildServerName ?? stored.BuildServerName;
        stored.RawJson = candidate.RawJson;
        stored.LastUpdatedAt = now;

        if (statusChanged)
        {
            if (stored.Status.IsFailure())
            {
                await ApplyFailureAsync(stored, now, ct);
            }

            AddEvent(
                stored.DeploymentId,
                stored.Status.IsActive() ? DeploymentEventType.Started : DeploymentEventType.Finished,
                previousStatus,
                stored.Status,
                now,
                stored.ErrorMessage ?? stored.Title);

            logger.LogInformation(
                "Deployment {DeploymentId} ({Service}) {From} -> {To}",
                stored.DeploymentId,
                stored.DisplayName,
                previousStatus,
                stored.Status);
        }

        return true;
    }

    /// <summary>Hatali deployment icin imza uretir ve (acikca istenmisse) logu arsivler.</summary>
    private async Task ApplyFailureAsync(TrackedDeployment deployment, DateTimeOffset now, CancellationToken ct)
    {
        if (ErrorSignatureExtractor.Extract(deployment.ErrorMessage) is { } signature)
        {
            deployment.ErrorSignatureHash = signature.Hash;

            var record = await db.ErrorSignatures.FindAsync([signature.Hash], ct);
            if (record is null)
            {
                db.ErrorSignatures.Add(new ErrorSignature
                {
                    Hash = signature.Hash,
                    NormalizedMessage = signature.NormalizedMessage,
                    SampleMessage = signature.SampleMessage,
                    OccurrenceCount = 1,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    LastServiceName = deployment.DisplayName,
                });
            }
            else
            {
                record.OccurrenceCount++;
                record.LastSeenAt = now;
                record.LastServiceName = deployment.DisplayName;
            }
        }

        if (_logOptions.ArchiveFailedDeployments && deployment.ArchivedLogPath is null)
        {
            deployment.ArchivedLogPath = await logReader.ArchiveAsync(deployment.LogPath, deployment.DeploymentId, ct);
        }
    }

    private void AddEvent(
        string deploymentId,
        DeploymentEventType type,
        DeploymentStatus? from,
        DeploymentStatus? to,
        DateTimeOffset at,
        string? message)
    {
        db.DeploymentEvents.Add(new DeploymentEvent
        {
            DeploymentId = deploymentId,
            EventType = type,
            Source = DeploymentEventSource.Poll,
            FromStatus = from,
            ToStatus = to,
            Message = message is null ? null : message[..Math.Min(message.Length, 1000)],
            OccurredAt = at,
        });
    }
}
