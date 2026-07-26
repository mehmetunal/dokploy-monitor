using DokployMonitor.Infrastructure.Identity;
using DokployMonitor.Web.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Services;

/// <summary>
/// Rolleri ve ilk yonetici hesabini olusturur. Kayit ekrani yok; panele ilk giris
/// varsayilan hesapla yapilir ve kullanici **kimlik bilgilerini degistirmeye zorlanir**.
/// Veritabaninda kullanici varsa hesap kismina dokunulmaz — parolalar asla degismez.
/// </summary>
public static class IdentitySeeder
{
    /// <summary>
    /// Auth:AdminPassword bos ise kullanilan varsayilan parola. Bu parolayla olusan hesap
    /// <see cref="ApplicationUser.MustChangeCredentials"/> bayragiyla isaretlenir; kullanici
    /// e-posta ve parolasini degistirmeden panele giremez.
    /// </summary>
    public const string DefaultAdminPassword = "Super123!";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var logger = services.GetRequiredService<ILogger<ApplicationUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in MonitorRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole(role));
                logger.LogInformation("Rol olusturuldu: {Role}", role);
            }
        }

        var options = services.GetRequiredService<IOptions<AuthOptions>>().Value;

        if (await users.Users.AnyAsync(ct))
        {
            await EnsureSuperAdminExistsAsync(users, options, logger, ct);
            return;
        }

        var usingDefault = string.IsNullOrWhiteSpace(options.AdminPassword);
        var password = usingDefault ? DefaultAdminPassword : options.AdminPassword!;

        var user = new ApplicationUser
        {
            UserName = options.AdminEmail,
            Email = options.AdminEmail,
            EmailConfirmed = true,
            DisplayName = options.AdminDisplayName,
            CreatedAt = DateTimeOffset.UtcNow,

            // Varsayilan parola kullanildiysa ilk giriste degistirmek zorunlu.
            MustChangeCredentials = usingDefault,
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError(
                "Yonetici kullanicisi olusturulamadi: {Errors}",
                string.Join(" | ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));
            return;
        }

        await users.AddToRoleAsync(user, MonitorRoles.SuperAdmin);

        if (usingDefault)
        {
            logger.LogWarning(
                "Yonetici hesabi varsayilan kimlik bilgileriyle olusturuldu ({Email} / {Password}). "
                + "Ilk giriste e-posta ve parola degistirilmesi zorunlu tutulacak.",
                options.AdminEmail,
                DefaultAdminPassword);
        }
        else
        {
            logger.LogInformation(
                "Yonetici hesabi olusturuldu: {Email} (rol: {Role})",
                options.AdminEmail,
                MonitorRoles.SuperAdmin);
        }
    }

    /// <summary>
    /// Roller sonradan eklendigi icin daha once olusmus hesaplarda SuperAdmin bulunmayabilir.
    /// Bu durumda kimse kullanici yonetemez ve deployment mudahalesi yapamaz — panele
    /// kilitlenmeyi onlemek icin yapilandirilmis yonetici (yoksa en eski hesap) yukseltilir.
    /// </summary>
    private static async Task EnsureSuperAdminExistsAsync(
        UserManager<ApplicationUser> users,
        AuthOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        if ((await users.GetUsersInRoleAsync(MonitorRoles.SuperAdmin)).Count > 0)
        {
            return;
        }

        var candidate = await users.FindByEmailAsync(options.AdminEmail)
            ?? await users.Users.OrderBy(user => user.CreatedAt).FirstOrDefaultAsync(ct);

        if (candidate is null)
        {
            return;
        }

        await users.AddToRoleAsync(candidate, MonitorRoles.SuperAdmin);

        logger.LogWarning(
            "Hicbir hesapta {Role} rolu yoktu; {Email} bu role yukseltildi.",
            MonitorRoles.SuperAdmin,
            candidate.Email);
    }
}
