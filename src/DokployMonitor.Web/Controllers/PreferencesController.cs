using DokployMonitor.Web.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace DokployMonitor.Web.Controllers;

/// <summary>
/// Arayuz tercihleri. Dil secimi cereze yazilir (RequestLocalization ayni cerezi okur);
/// tema tarayici tarafinda ayarlandigi icin burada yalnizca dil ucu var.
/// Giris ekraninda da calismasi gerektigi icin anonim.
/// </summary>
[AllowAnonymous]
public sealed class PreferencesController : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string culture, string? returnUrl)
    {
        // Yalnizca destekledigimiz diller kabul edilir.
        if (LocalizationSetup.IsSupported(culture))
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                });
        }

        // Acik yonlendirme acigina karsi yalnizca yerel adresler.
        return returnUrl is { Length: > 0 } && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Dashboard");
    }
}
