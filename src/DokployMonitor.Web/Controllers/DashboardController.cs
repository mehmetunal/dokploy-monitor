using DokployMonitor.Core.Abstractions;
using DokployMonitor.Infrastructure.Caching;
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
        [FromServices] IDokployClientFactory clientFactory,
        [FromServices] ConnectionService connections,
        [FromServices] IContainerLogReader containerLogReader,
        [FromServices] CacheService cache,
        CancellationToken ct)
    {
        // Her baglanti ayri kontrol edilir: biri bozuksa hangisi oldugu gorunur.
        var results = new List<ConnectionHealth>();

        foreach (var connection in await connections.GetAllAsync(ct))
        {
            results.Add(new ConnectionHealth
            {
                Connection = connection,
                Health = connection.Enabled
                    ? await clientFactory.Create(connection).CheckHealthAsync(ct)
                    : null,
            });
        }

        return View(new DiagnosticsViewModel
        {
            Connections = results,
            Docker = await containerLogReader.CheckHealthAsync(ct),
            Cache = await cache.CheckHealthAsync(ct),
        });
    }

    /// <summary>Hata sayfasi: giris yapilmamis istekler de buraya dusebilir.</summary>
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Error() => View();
}
