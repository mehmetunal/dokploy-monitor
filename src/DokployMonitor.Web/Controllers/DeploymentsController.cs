using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.Deployments;
using DokployMonitor.Infrastructure.Identity;
using DokployMonitor.Infrastructure.Logs;
using DokployMonitor.Web.Models;
using DokployMonitor.Web.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Controllers;

public sealed class DeploymentsController(
    DashboardQueryService dashboard,
    IDeploymentLogReader logReader,
    IContainerLogReader containerLogReader,
    IDokployClientFactory clientFactory,
    ConnectionService connections,
    MonitorState state,
    IOptions<LogOptions> logOptions,
    ILogger<DeploymentsController> logger) : Controller
{
    private readonly LogOptions _logOptions = logOptions.Value;

    /// <summary>
    /// Filtrelenebilir deployment gecmisi. Filtre FluentValidation ile dogrulanir;
    /// gecersiz parametrede sorgu hic calistirilmaz, ekranda sebep gosterilir.
    /// </summary>
    public async Task<IActionResult> Index(
        [FromQuery] DeploymentFilter filter,
        [FromServices] IValidator<DeploymentFilter> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(filter, ct);
        foreach (var error in validation.Errors)
        {
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        IReadOnlyList<TrackedDeployment> results = validation.IsValid
            ? await dashboard.SearchAsync(filter, ct)
            : [];

        return View(new DeploymentHistoryViewModel
        {
            Deployments = results,
            Projects = await dashboard.GetProjectNamesAsync(ct),
            Filter = filter,
            ConnectionNames = await connections.GetNamesAsync(ct),
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
                : await dashboard.GetServiceHistoryAsync(deployment.ServiceId, take: 25, ct),
            ProjectHistory = deployment.ProjectName is null
                ? []
                : await dashboard.GetProjectHistoryAsync(deployment.ProjectName, deployment.ServiceId, take: 10, ct),
            Log = log,
            CanStreamLive = deployment.Status.IsActive() && log.Available,
        });
    }

    /// <summary>
    /// Log kuyrugunu HTTP ile cekmek icin: SignalR kullanilamadiginda ve liste
    /// ekranlarindaki log onizlemesinde kullanilir.
    ///
    /// <paramref name="source"/>: <c>docker</c> = calisan servisin container logu
    /// (Engine API), <c>build</c> = Dokploy'un derleme logu (dosya). Bos birakilirsa
    /// once container logu denenir, yoksa build logune dusulur.
    /// </summary>
    [HttpGet("deployments/{id}/log")]
    public async Task<IActionResult> Log(
        string id,
        long offset,
        int? tail,
        string? source,
        CancellationToken ct)
    {
        var deployment = await dashboard.FindAsync(id, ct);
        if (deployment is null)
        {
            return NotFound();
        }

        var lines = Math.Clamp(tail ?? _logOptions.DefaultTailLines, 20, _logOptions.DefaultTailLines);
        var wantsBuild = string.Equals(source, "build", StringComparison.OrdinalIgnoreCase);

        LogReadResult result;
        string usedSource;

        if (wantsBuild)
        {
            result = await ReadBuildLogAsync(deployment, lines, ct);
            usedSource = "build";
        }
        else
        {
            result = await containerLogReader.ReadTailAsync(ContainerName(deployment), lines, ct);
            usedSource = "docker";

            // Otomatik mod: container logu yoksa (silinmis servis, hatali build) build logu.
            if (!result.Available && string.IsNullOrWhiteSpace(source))
            {
                var build = await ReadBuildLogAsync(deployment, lines, ct);
                if (build.Available)
                {
                    result = build;
                    usedSource = "build";
                }
            }
        }

        return Json(new
        {
            result.Lines,
            result.Offset,
            result.Available,
            result.UnavailableReason,
            source = usedSource,
            requestedOffset = offset,
        });
    }

    /// <summary>Docker tarafindaki ad: Dokploy servisleri appName ile calisir.</summary>
    private static string? ContainerName(TrackedDeployment deployment) =>
        !string.IsNullOrWhiteSpace(deployment.AppName) ? deployment.AppName : deployment.ServiceName;

    /// <summary>Build logu: canli dosya yoksa arsivlenmis kopya.</summary>
    private async Task<LogReadResult> ReadBuildLogAsync(
        TrackedDeployment deployment,
        int lines,
        CancellationToken ct)
    {
        var result = await logReader.ReadTailAsync(deployment.LogPath, lines, ct);
        if (!result.Available && deployment.ArchivedLogPath is not null)
        {
            result = await ReadArchivedAsync(deployment.ArchivedLogPath, ct, lines);
        }

        return result;
    }

    /// <summary>Deployment durdurma — yalnizca SuperAdmin.</summary>
    [HttpPost]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Kill(string id, CancellationToken ct)
    {
        var deployment = await dashboard.FindAsync(id, ct);
        if (deployment is null)
        {
            return NotFound();
        }

        var client = await ResolveClientAsync(deployment, ct);
        if (client is null)
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await client.KillDeploymentAsync(id, ct);
            state.RequestSync(SyncTrigger.UserAction);
            TempData["Message"] = "Deployment durdurma istegi gonderildi.";
            logger.LogInformation(
                "Deployment durduruldu: {DeploymentId} (islem: {Actor})", id, User.Identity?.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deployment durdurulamadi: {DeploymentId}", id);
            TempData["Error"] = $"Durdurulamadi: {ex.Message}";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Yeniden deploy — yalnizca SuperAdmin.</summary>
    [HttpPost]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Redeploy(string id, CancellationToken ct) =>
        TriggerDeployAsync(id, replay: false, ct);

    /// <summary>
    /// Replay: eski bir deployment kaydindan ayni servisin deploy'unu tekrar tetikler.
    /// Dokploy API'si belirli bir commit'i deploy edemedigi icin kaynagin **guncel**
    /// hali derlenir; kayit yalnizca hangi servisin ve hangi deployment'in tekrarlandigini
    /// belirler (baslikta gorunur). Yalnizca SuperAdmin.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Replay(string id, CancellationToken ct) =>
        TriggerDeployAsync(id, replay: true, ct);

    private async Task<IActionResult> TriggerDeployAsync(string id, bool replay, CancellationToken ct)
    {
        var deployment = await dashboard.FindAsync(id, ct);
        if (deployment is null)
        {
            return NotFound();
        }

        var client = await ResolveClientAsync(deployment, ct);
        if (client is null)
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        var title = replay
            ? $"Replay: {deployment.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} ({deployment.DeploymentId})"
            : "Redeploy (Monitor)";

        try
        {
            if (deployment.ComposeId is { } composeId)
            {
                await client.RedeployComposeAsync(composeId, title, ct);
            }
            else if (deployment.ApplicationId is { } applicationId)
            {
                await client.RedeployApplicationAsync(applicationId, title, ct);
            }
            else
            {
                TempData["Error"] = "Bu deployment turu panelden yeniden baslatilamiyor.";
                return RedirectToAction(nameof(Details), new { id });
            }

            state.RequestSync(SyncTrigger.UserAction);
            TempData["Message"] = replay
                ? "Replay istegi kuyruga eklendi. Not: Dokploy kaynagin guncel halini derler."
                : "Yeniden deploy istegi kuyruga eklendi.";

            logger.LogInformation(
                "{Action} tetiklendi: {DeploymentId} (islem: {Actor})",
                replay ? "Replay" : "Redeploy",
                id,
                User.Identity?.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Action} basarisiz: {DeploymentId}", replay ? "Replay" : "Redeploy", id);
            TempData["Error"] = $"{(replay ? "Replay" : "Yeniden deploy")} basarisiz: {ex.Message}";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Kaydin geldigi Dokploy baglantisi icin istemci uretir. Coklu baglanti oncesinde
    /// toplanan kayitlarda baglanti bilgisi olmadigi icin tek etkin baglanti varsa ona duser.
    /// </summary>
    private async Task<IDokployClient?> ResolveClientAsync(TrackedDeployment deployment, CancellationToken ct)
    {
        var enabled = await connections.GetEnabledAsync(ct);

        var connection = deployment.ConnectionId is { } id
            ? enabled.FirstOrDefault(c => c.Id == id)
            : enabled.Count == 1 ? enabled[0] : null;

        if (connection is null)
        {
            TempData["Error"] = deployment.ConnectionId is null
                ? "Bu kaydin hangi Dokploy baglantisindan geldigi bilinmiyor; birden fazla baglanti tanimli oldugu icin islem yapilamadi."
                : "Kaydin baglantisi bulunamadi ya da devre disi birakilmis.";

            return null;
        }

        return clientFactory.Create(connection);
    }

    private static async Task<LogReadResult> ReadArchivedAsync(
        string archivedPath,
        CancellationToken ct,
        int? tail = null)
    {
        try
        {
            var lines = await System.IO.File.ReadAllLinesAsync(archivedPath, ct);
            if (tail is { } count && lines.Length > count)
            {
                lines = [.. lines[^count..]];
            }

            return new LogReadResult(lines, 0, true, null);
        }
        catch (IOException)
        {
            return new LogReadResult([], 0, false, "Arsivlenmis log okunamadi.");
        }
    }
}
