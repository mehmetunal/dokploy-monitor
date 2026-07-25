using DokployMonitor.Infrastructure.Identity;
using DokployMonitor.Web.Models;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DokployMonitor.Web.Controllers;

/// <summary>
/// Panel girisi. Kayit ve parola sifirlama ekrani bilinceli olarak yok: hesaplar
/// yalnizca acilistaki seed ile ya da elle olusturulur.
/// </summary>
public sealed class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IValidator<LoginInput> validator,
    ILogger<AccountController> logger) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl)
    {
        // Zaten giris yapilmissa dogrudan panoya.
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        return View(new LoginInput { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInput input, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        foreach (var error in validation.Errors)
        {
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        if (!validation.IsValid)
        {
            return View(input);
        }

        var user = await userManager.FindByEmailAsync(input.Email!);
        if (user is null)
        {
            // Kullanici var mi yok mu bilgisini sizdirmamak icin tek ve ayni mesaj.
            ModelState.AddModelError(string.Empty, "E-posta veya parola hatali.");
            return View(input);
        }

        var result = await signInManager.PasswordSignInAsync(
            user, input.Password!, input.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            logger.LogWarning("Hesap gecici olarak kilitli: {Email}", input.Email);
            ModelState.AddModelError(string.Empty, "Cok fazla hatali deneme. Bir sure sonra tekrar deneyin.");
            return View(input);
        }

        if (!result.Succeeded)
        {
            logger.LogWarning("Basarisiz giris denemesi: {Email}", input.Email);
            ModelState.AddModelError(string.Empty, "E-posta veya parola hatali.");
            return View(input);
        }

        logger.LogInformation("Giris yapildi: {Email}", input.Email);
        return RedirectToLocal(input.ReturnUrl);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    /// <summary>Acik yonlendirme (open redirect) acigini kapatmak icin yalnizca yerel adresler.</summary>
    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Dashboard");
}
