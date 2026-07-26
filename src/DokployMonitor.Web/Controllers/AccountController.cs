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

    /// <summary>Yetkisi olmayan kullanici (or. Viewer, SuperAdmin islemi denerse) buraya duser.</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    /// <summary>
    /// Varsayilan kimlik bilgileriyle olusturulan hesabin ilk girisinde zorunlu adim:
    /// e-posta ve parola birlikte degistirilir (bkz. RequireCredentialChangeFilter).
    /// </summary>
    [HttpGet]
    [Authorize]
    public IActionResult ChangeCredentials()
    {
        ViewData["Forced"] = User.HasClaim(MonitorClaims.MustChangeCredentials, "true");
        return View(new ChangeCredentialsInput { NewEmail = User.Identity?.Name });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeCredentials(
        ChangeCredentialsInput input,
        [FromServices] IValidator<ChangeCredentialsInput> credentialsValidator,
        CancellationToken ct)
    {
        var forced = User.HasClaim(MonitorClaims.MustChangeCredentials, "true");
        ViewData["Forced"] = forced;

        var validation = await credentialsValidator.ValidateAsync(input, ct);
        foreach (var error in validation.Errors)
        {
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        if (!await userManager.CheckPasswordAsync(user, input.CurrentPassword ?? string.Empty))
        {
            ModelState.AddModelError(nameof(input.CurrentPassword), "Mevcut parola hatali.");
        }

        // Zorunlu adimda e-posta da gercekten degismeli: varsayilan hesap kalmasin.
        if (forced && string.Equals(user.Email, input.NewEmail, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                nameof(input.NewEmail),
                "Varsayilan e-postadan farkli bir adres girmelisiniz.");
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        if (!string.Equals(user.Email, input.NewEmail, StringComparison.OrdinalIgnoreCase))
        {
            var emailResult = await userManager.SetEmailAsync(user, input.NewEmail);
            var nameResult = await userManager.SetUserNameAsync(user, input.NewEmail);

            if (!AddErrors(emailResult) || !AddErrors(nameResult))
            {
                return View(input);
            }

            user.EmailConfirmed = true;
        }

        var passwordResult = await userManager.ChangePasswordAsync(user, input.CurrentPassword!, input.NewPassword!);
        if (!AddErrors(passwordResult))
        {
            return View(input);
        }

        user.MustChangeCredentials = false;
        await userManager.UpdateAsync(user);

        // Cerezdeki talepler tazelenir; "degistirmelisin" bayragi hemen kalkar.
        await signInManager.RefreshSignInAsync(user);

        logger.LogInformation("Kimlik bilgileri guncellendi: {Email}", user.Email);
        TempData["Message"] = "Kimlik bilgileriniz guncellendi.";

        return RedirectToAction("Index", "Dashboard");
    }

    /// <summary>Identity hatalarini ModelState'e tasir; islem basarili ise true.</summary>
    private bool AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return result.Succeeded;
    }

    /// <summary>Acik yonlendirme (open redirect) acigini kapatmak icin yalnizca yerel adresler.</summary>
    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Dashboard");
}
