using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.Deployments;
using DokployMonitor.Infrastructure.Logs;
using DokployMonitor.Web.Models;
using DokployMonitor.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Controllers;

public sealed class DeploymentsController(
    DashboardQueryService dashboard,
    IDeploymentLogReader logReader,
    IDokployClient dokploy,
    MonitorState state,
    IOptions<LogOptions> logOptions,
    ILogger<DeploymentsController> logger) : Controller
{
    private readonly LogOptions _logOptions = logOptions.Value;

    /// <summary>Filtrelenebilir deployment gecmisi.</summary>
    public async Task<IActionResult> Index(string? project, string? status, string? q, CancellationToken ct)
    {
        var results = await dashboard.SearchAsync(project, status, q, take: 200, ct);

        return View(new DeploymentHistoryViewModel
        {
            Deployments = results,
            Projects = await dashboard.GetProjectNamesAsync(ct),
            SelectedProject = project,
            SelectedStatus = status,
            Query = q,
        });
    }

    public async Task<IActionResult> Details(string id, CancellationToken ct)
    {
        var deployment = await dashboard.FindAsync(id, ct);
        if (deployment is null)
        {
            return NotFound();
        }

        // Log oncelikle Dokploy'un canli dosyasindan; yoksa (temizlenmisse) arsivden okunur.
        var log = await logReader.ReadTailAsync(deployment.LogPath, _logOptions.DefaultTailLines, ct);
        if (!log.Available && deployment.ArchivedLogPath is not null)
        {
            log = await ReadArchivedAsync(deployment.ArchivedLogPath, ct);
        }

        return View(new DeploymentDetailsViewModel
        {
            Deployment = deployment,
            Events = await dashboard.GetEventsAsync(id, ct),
            History = deployment.ServiceId is null
                ? []
                : await dashboard.GetServiceHistoryAsync(deployment.ServiceId, take: 15, ct),
            Log = log,
            CanStreamLive = deployment.Status.IsActive() && log.Available,
        });
    }

    /// <summary>SignalR kullanilamadiginda log kuyrugunu HTTP ile cekmek icin.</summary>
    [HttpGet("deployments/{id}/log")]
    public async Task<IActionResult> Log(string id, long offset, CancellationToken ct)
    {
        var deployment = await dashboard.FindAsync(id, ct);
        if (deployment is null)
        {
            return NotFound();
        }

        var result = await logReader.ReadTailAsync(deployment.LogPath, _logOptions.DefaultTailLines, ct);
        return Json(new { result.Lines, result.Offset, result.Available, result.UnavailableReason, requestedOffset = offset });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Kill(string id, CancellationToken ct)
    {
        try
        {
            await dokploy.KillDeploymentAsync(id, ct);
            state.RequestSync(SyncTrigger.UserAction);
            TempData["Message"] = "Deployment durdurma istegi gonderildi.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deployment durdurulamadi: {DeploymentId}", id);
            TempData["Error"] = $"Durdurulamadi: {ex.Message}";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Redeploy(string id, CancellationToken ct)
    {
        var deployment = await dashboard.FindAsync(id, ct);
        if (deployment is null)
        {
            return NotFound();
        }

        try
        {
            if (deployment.ComposeId is { } composeId)
            {
                await dokploy.RedeployComposeAsync(composeId, ct: ct);
            }
            else if (deployment.ApplicationId is { } applicationId)
            {
                await dokploy.RedeployApplicationAsync(applicationId, ct: ct);
            }
            else
            {
                TempData["Error"] = "Bu deployment turu panelden yeniden baslatilamiyor.";
                return RedirectToAction(nameof(Details), new { id });
            }

            state.RequestSync(SyncTrigger.UserAction);
            TempData["Message"] = "Yeniden deploy istegi kuyruga eklendi.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Yeniden deploy basarisiz: {DeploymentId}", id);
            TempData["Error"] = $"Yeniden deploy basarisiz: {ex.Message}";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private static async Task<LogReadResult> ReadArchivedAsync(string archivedPath, CancellationToken ct)
    {
        try
        {
            var lines = await System.IO.File.ReadAllLinesAsync(archivedPath, ct);
            return new LogReadResult(lines, 0, true, null);
        }
        catch (IOException)
        {
            return new LogReadResult([], 0, false, "Arsivlenmis log okunamadi.");
        }
    }
}
