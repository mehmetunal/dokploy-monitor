using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.Dokploy;
using DokployMonitor.Infrastructure.Identity;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Web.Models;
using DokployMonitor.Web.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;

namespace DokployMonitor.Web.Controllers;

/// <summary>
/// Izlenen Dokploy sunucularinin (API anahtarlarinin) yonetimi — yalnizca SuperAdmin.
/// Anahtarlar veritabaninda tutulur ve ekranda maskeli gosterilir.
/// </summary>
[Authorize(Roles = MonitorRoles.SuperAdmin)]
public sealed class ConnectionsController(
    MonitorDbContext db,
    ConnectionService connections,
    IDokployClientFactory clientFactory,
    MonitorState state,
    IValidator<ConnectionInput> validator,
    IStringLocalizer<SharedResource> L,
    ILogger<ConnectionsController> logger) : Controller
{
    /// <summary><paramref name="edit"/> verilirse form o baglantiyla doldurulur (anahtar haric).</summary>
    public async Task<IActionResult> Index(string? edit, CancellationToken ct)
    {
        var input = new ConnectionInput();

        if (!string.IsNullOrWhiteSpace(edit)
            && await db.Connections.FirstOrDefaultAsync(c => c.Id == edit, ct) is { } connection)
        {
            input = new ConnectionInput
            {
                Id = connection.Id,
                Name = connection.Name,
                BaseUrl = connection.BaseUrl,
                Enabled = connection.Enabled,
                AllowInvalidCertificates = connection.AllowInvalidCertificates,
                ForceLegacyDiscovery = connection.ForceLegacyDiscovery,
                TimeoutSeconds = connection.TimeoutSeconds,
                MaxParallelRequests = connection.MaxParallelRequests,
            };
        }

        return View(await BuildListAsync(input, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ConnectionInput input, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        foreach (var error in validation.Errors)
        {
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        var duplicate = await db.Connections.AnyAsync(
            connection => connection.Name == input.Name && connection.Id != input.Id, ct);

        if (duplicate)
        {
            ModelState.AddModelError(nameof(input.Name), L["Another connection with this name already exists."]);
        }

        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await BuildListAsync(input, ct));
        }

        var existing = string.IsNullOrWhiteSpace(input.Id)
            ? null
            : await db.Connections.FirstOrDefaultAsync(connection => connection.Id == input.Id, ct);

        if (existing is null)
        {
            db.Connections.Add(new DokployConnection
            {
                Id = Guid.NewGuid().ToString("n"),
                Name = input.Name!,
                BaseUrl = input.BaseUrl!.TrimEnd('/'),
                ApiKey = input.ApiKey!,
                Enabled = input.Enabled,
                AllowInvalidCertificates = input.AllowInvalidCertificates,
                ForceLegacyDiscovery = input.ForceLegacyDiscovery,
                TimeoutSeconds = input.TimeoutSeconds,
                MaxParallelRequests = input.MaxParallelRequests,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            TempData["Message"] = L["Connection '{0}' added.", input.Name!].Value;
        }
        else
        {
            existing.Name = input.Name!;
            existing.BaseUrl = input.BaseUrl!.TrimEnd('/');
            existing.Enabled = input.Enabled;
            existing.AllowInvalidCertificates = input.AllowInvalidCertificates;
            existing.ForceLegacyDiscovery = input.ForceLegacyDiscovery;
            existing.TimeoutSeconds = input.TimeoutSeconds;
            existing.MaxParallelRequests = input.MaxParallelRequests;

            // Bos birakildiysa mevcut anahtar korunur (ekranda hic gosterilmiyor).
            if (!string.IsNullOrWhiteSpace(input.ApiKey))
            {
                existing.ApiKey = input.ApiKey;
            }

            if (!existing.Enabled)
            {
                state.ForgetQueue(existing.Id);
            }

            TempData["Message"] = L["Connection '{0}' updated.", input.Name!].Value;
        }

        await db.SaveChangesAsync(ct);
        await connections.InvalidateNamesAsync(ct);
        state.RequestSync(SyncTrigger.UserAction);

        logger.LogInformation(
            "Dokploy baglantisi kaydedildi: {Name} (islem: {Actor})", input.Name, User.Identity?.Name);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>Baglantiyi kaydetmeden once dogrudan dener.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(string id, CancellationToken ct)
    {
        var connection = await db.Connections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (connection is null)
        {
            return NotFound();
        }

        var health = await clientFactory.Create(connection).CheckHealthAsync(ct);

        if (health is { IsReachable: true, IsAuthorized: true })
        {
            TempData["Message"] =
                $"'{connection.Name}' calisiyor · merkezi endpoint: {(health.SupportsCentralizedDeployments ? "var" : "yok")}"
                + $" · kuyruk: {(health.SupportsQueueList ? "var" : "yok")}";
        }
        else
        {
            TempData["Error"] = $"'{connection.Name}' basarisiz: {health.Error ?? L["unreachable"]}";
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Baglantiyi siler. Toplanan deployment kayitlari korunur (gecmis kaybolmasin);
    /// yalnizca baglanti etiketleri sahipsiz kalir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var connection = await db.Connections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (connection is null)
        {
            return NotFound();
        }

        db.Connections.Remove(connection);
        await db.SaveChangesAsync(ct);
        await connections.InvalidateNamesAsync(ct);
        state.ForgetQueue(id);

        logger.LogWarning(
            "Dokploy baglantisi silindi: {Name} (islem: {Actor})", connection.Name, User.Identity?.Name);

        TempData["Message"] = L["'{0}' deleted. Collected deployment history was kept.", connection.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    private async Task<ConnectionListViewModel> BuildListAsync(ConnectionInput input, CancellationToken ct)
    {
        var counts = await db.Deployments
            .Where(deployment => deployment.ConnectionId != null)
            .GroupBy(deployment => deployment.ConnectionId!)
            .Select(group => new { ConnectionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.ConnectionId, row => row.Count, ct);

        return new ConnectionListViewModel
        {
            Connections = await connections.GetAllAsync(ct),
            NewConnection = input,
            DeploymentCounts = counts,
        };
    }
}
