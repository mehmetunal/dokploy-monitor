using DokployMonitor.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DokployMonitor.Web.Filters;

/// <summary>
/// Kimlik bilgilerini degistirmesi gereken kullaniciyi panelin herhangi bir yerine
/// girmeden once degistirme ekranina yonlendirir. Kontrol oturum cerezindeki talep
/// uzerinden yapilir (bkz. <see cref="MonitorClaims.MustChangeCredentials"/>), bu
/// yuzden her istekte veritabanina gidilmez.
/// </summary>
public sealed class RequireCredentialChangeFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true
            || !user.HasClaim(MonitorClaims.MustChangeCredentials, "true"))
        {
            await next();
            return;
        }

        // Anonim uc noktalar (webhook, giris) ve degistirme/cikis akisi serbest.
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            await next();
            return;
        }

        var routeValues = context.RouteData.Values;
        var controller = routeValues["controller"]?.ToString();
        var action = routeValues["action"]?.ToString();

        var allowed = string.Equals(controller, "Account", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(action, "ChangeCredentials", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Logout", StringComparison.OrdinalIgnoreCase));

        if (allowed)
        {
            await next();
            return;
        }

        // JSON isteyen istemciye (pano polling'i) yonlendirme yerine net bir durum kodu.
        if (context.HttpContext.Request.Headers.Accept.Any(
                value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }

        context.Result = new RedirectToActionResult("ChangeCredentials", "Account", null);
    }
}
