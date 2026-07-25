using System.Runtime.CompilerServices;
using DokployMonitor.Core.Abstractions;
using DokployMonitor.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DokployMonitor.Web.Hubs;

/// <summary>
/// Tarayiciya canli veri akisi:
///  - "dashboard" olayi: pano anlik goruntusu degistiginde sunucu tarafindan gonderilir.
///  - StreamLogs: bir deployment'in build logunu satir satir akitir.
/// </summary>
public sealed class DeploymentsHub(
    IDbContextFactory<MonitorDbContext> dbFactory,
    IDeploymentLogReader logReader) : Hub
{
    /// <summary>Deployment loglarini <paramref name="fromOffset"/> byte'indan itibaren akitir.</summary>
    public async IAsyncEnumerable<LogChunk> StreamLogs(
        string deploymentId,
        long fromOffset,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var logPath = await db.Deployments
            .Where(d => d.DeploymentId == deploymentId)
            .Select(d => d.LogPath)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(logPath))
        {
            yield break;
        }

        await foreach (var chunk in logReader.StreamAsync(logPath, fromOffset, ct))
        {
            yield return chunk;
        }
    }
}
