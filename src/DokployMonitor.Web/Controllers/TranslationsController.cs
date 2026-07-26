using DokployMonitor.Core.Localization;
using DokployMonitor.Infrastructure.Identity;
using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Web.Models;
using DokployMonitor.Web.Options;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;

namespace DokployMonitor.Web.Controllers;

/// <summary>
/// Arayuz cevirileri — yalnizca SuperAdmin. Ceviriler veritabanindadir; kaydedilen
/// degisiklik aninda uygulanir (anlik goruntu yeniden yuklenir).
/// </summary>
[Authorize(Roles = MonitorRoles.SuperAdmin)]
public sealed class TranslationsController(
    MonitorDbContext db,
    TranslationStore store,
    IValidator<TranslationInput> validator,
    IStringLocalizer<SharedResource> L,
    ILogger<TranslationsController> logger) : Controller
{
    public async Task<IActionResult> Index(
        string? culture,
        bool onlyMissing,
        string? search,
        int? page,
        int? size,
        CancellationToken ct)
    {
        var selected = Normalize(culture);

        var query = db.Translations.AsNoTracking().Where(row => row.Culture == selected);

        var total = await query.CountAsync(ct);
        var missing = await query.CountAsync(row => row.Value == null || row.Value == string.Empty, ct);

        if (onlyMissing)
        {
            query = query.Where(row => row.Value == null || row.Value == string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(row =>
                EF.Functions.Like(row.Key, $"%{term}%")
                || (row.Value != null && EF.Functions.Like(row.Value, $"%{term}%")));
        }

        var paging = PageInfo.Create(page, size, await query.CountAsync(ct));

        return View(new TranslationListViewModel
        {
            Culture = selected,
            Rows = await query.OrderBy(row => row.Key).Skip(paging.Skip).Take(paging.Size).ToListAsync(ct),
            OnlyMissing = onlyMissing,
            Search = search,
            TotalCount = total,
            MissingCount = missing,
            LoadedAt = store.LoadedAt,
            Page = paging,
        });
    }

    /// <summary>Ekranda gorunen satirlarin degerlerini toplu kaydeder.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        string culture,
        [FromForm] Dictionary<string, string?> values,
        bool onlyMissing,
        string? search,
        CancellationToken ct)
    {
        var selected = Normalize(culture);
        var changed = 0;

        foreach (var (key, value) in values)
        {
            var row = await db.Translations
                .FirstOrDefaultAsync(item => item.Culture == selected && item.Key == key, ct);

            if (row is null)
            {
                continue;
            }

            var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (row.Value == trimmed)
            {
                continue;
            }

            row.Value = trimmed;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedBy = User.Identity?.Name;
            changed++;
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct);

            // Degisiklik aninda gorunsun: bellek ici anlik goruntuyu tazele.
            await store.ReloadAsync(ct);

            logger.LogInformation(
                "{Count} ceviri guncellendi ({Culture}) — islem: {Actor}",
                changed, selected, User.Identity?.Name);

            TempData["Message"] = L["{0} translations updated ({1}).", changed, selected].Value;
        }
        else
        {
            TempData["Message"] = L["No changes."].Value;
        }

        return RedirectToAction(nameof(Index), new { culture = selected, onlyMissing, search });
    }

    /// <summary>Yeni anahtar ekler (ekranda henuz gorulmemis bir metin icin).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(TranslationInput input, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
        {
            TempData["Error"] = string.Join(" · ", validation.Errors.Select(error => error.ErrorMessage));
            return RedirectToAction(nameof(Index), new { culture = input.Culture });
        }

        var selected = Normalize(input.Culture);
        var key = input.Key!.Trim();

        if (await db.Translations.AnyAsync(row => row.Culture == selected && row.Key == key, ct))
        {
            TempData["Error"] = L["This key already exists for this language."].Value;
            return RedirectToAction(nameof(Index), new { culture = selected, search = key });
        }

        db.Translations.Add(new Translation
        {
            Culture = selected,
            Key = key,
            Value = string.IsNullOrWhiteSpace(input.Value) ? null : input.Value.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = User.Identity?.Name,
        });

        await db.SaveChangesAsync(ct);
        await store.ReloadAsync(ct);

        TempData["Message"] = L["'{0}' added ({1}).", key, selected].Value;
        return RedirectToAction(nameof(Index), new { culture = selected, search = key });
    }

    /// <summary>Eksik ceviri satirlarini yeniden tarar (ekranlarda gorulen anahtarlari yakalar).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(string? culture, CancellationToken ct)
    {
        await store.ReloadAsync(ct);
        TempData["Message"] = L["Translations reloaded."].Value;
        return RedirectToAction(nameof(Index), new { culture });
    }

    private static string Normalize(string? culture) =>
        LocalizationSetup.IsSupported(culture)
            ? culture!.ToLowerInvariant()
            : LocalizationSetup.Supported
                .First(item => item.Code != LocalizationSetup.DefaultCulture).Code;
}
