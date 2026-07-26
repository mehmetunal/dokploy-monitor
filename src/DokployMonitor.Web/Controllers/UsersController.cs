using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Infrastructure.Identity;
using DokployMonitor.Web.Models;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;

namespace DokployMonitor.Web.Controllers;

/// <summary>
/// Kullanici yonetimi — yalnizca SuperAdmin. Kayit (self-service) ekrani yoktur:
/// hesaplari yalnizca bir SuperAdmin olusturur.
/// </summary>
[Authorize(Roles = MonitorRoles.SuperAdmin)]
public sealed class UsersController(
    UserManager<ApplicationUser> userManager,
    IValidator<CreateUserInput> validator,
    IStringLocalizer<SharedResource> L,
    ILogger<UsersController> logger) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await BuildListAsync(new CreateUserInput(), ct));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserInput input, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        foreach (var error in validation.Errors)
        {
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        if (await userManager.FindByEmailAsync(input.Email ?? string.Empty) is not null)
        {
            ModelState.AddModelError(nameof(input.Email), L["A user with this email already exists."]);
        }

        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await BuildListAsync(input, ct));
        }

        var user = new ApplicationUser
        {
            UserName = input.Email,
            Email = input.Email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName,
            CreatedAt = DateTimeOffset.UtcNow,
            MustChangeCredentials = input.MustChangeCredentials,
        };

        var result = await userManager.CreateAsync(user, input.Password!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(nameof(Index), await BuildListAsync(input, ct));
        }

        await userManager.AddToRoleAsync(user, input.Role);

        logger.LogInformation(
            "Kullanici olusturuldu: {Email} (rol: {Role}) — olusturan: {Actor}",
            user.Email,
            input.Role,
            User.Identity?.Name);

        TempData["Message"] = L["{0} created ({1}).", user.Email!, input.Role].Value;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Kullaniciyi devre disi birakir (kalici silme yerine kilitleme).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLock(string id, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        // Kendi hesabini kilitleyip panelden kilitlenmeyi engelle.
        if (string.Equals(user.Id, userManager.GetUserId(User), StringComparison.Ordinal))
        {
            TempData["Error"] = L["You cannot lock your own account."].Value;
            return RedirectToAction(nameof(Index));
        }

        var locked = user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow;
        await userManager.SetLockoutEndDateAsync(user, locked ? null : DateTimeOffset.UtcNow.AddYears(100));

        TempData["Message"] = locked
            ? L["{0} unlocked.", user.Email!].Value
            : L["{0} locked.", user.Email!].Value;

        logger.LogInformation(
            "Kullanici kilit durumu degisti: {Email} -> {State} (islem: {Actor})",
            user.Email,
            locked ? "acik" : "kilitli",
            User.Identity?.Name);

        return RedirectToAction(nameof(Index));
    }

    private async Task<UserListViewModel> BuildListAsync(CreateUserInput input, CancellationToken ct)
    {
        var currentUserId = userManager.GetUserId(User);
        var users = await userManager.Users.OrderBy(user => user.Email).ToListAsync(ct);
        var rows = new List<UserRow>(users.Count);

        foreach (var user in users)
        {
            rows.Add(new UserRow
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? user.Id,
                DisplayName = user.DisplayName,
                Roles = [.. await userManager.GetRolesAsync(user)],
                CreatedAt = user.CreatedAt,
                MustChangeCredentials = user.MustChangeCredentials,
                LockedOut = user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow,
                IsCurrentUser = string.Equals(user.Id, currentUserId, StringComparison.Ordinal),
            });
        }

        return new UserListViewModel { Users = rows, NewUser = input };
    }
}
