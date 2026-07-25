using DokployMonitor.Core.Abstractions;
using DokployMonitor.Web.Models;
using DokployMonitor.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DokployMonitor.Web.Controllers;

public sealed class DashboardController(DashboardQueryService dashboard) : Controller
{
    /// <summary>Canli pano. Ilk yukleme sunucu tarafinda render edilir, sonrasi SignalR ile guncellenir.</summary>
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var snapshot = await dashboard.GetSnapshotAsync(ct);
        return View(snapshot);
    }

    /// <summary>
    /// Panonun JSON hali. SignalR baglantisi kurulamazsa istemci buraya donerek
    /// periyodik olarak veri ceker (graceful degradation).
    /// </summary>
    [HttpGet("dashboard/snapshot")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Snapshot(CancellationToken ct) =>
        Json(await dashboard.GetSnapshotAsync(ct));

    /// <summary>Dokploy baglantisi, container log erisimi ve yetenek tespiti.</summary>
    public async Task<IActionResult> Diagnostics(
        [FromServices] IDokployClient dokploy,
        [FromServices] IContainerLogReader containerLogReader,
        CancellationToken ct)
    {
        return View(new DiagnosticsViewModel
        {
            Dokploy = await dokploy.CheckHealthAsync(ct),
            Docker = await containerLogReader.CheckHealthAsync(ct),
        });
    }

    /// <summary>Hata sayfasi: giris yapilmamis istekler de buraya dusebilir.</summary>
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Error() => View();
}
