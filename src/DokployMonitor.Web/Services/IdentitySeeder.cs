using System.Security.Cryptography;
using DokployMonitor.Infrastructure.Identity;
using DokployMonitor.Web.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Services;

/// <summary>
/// Ilk yonetici kullanicisini olusturur. Kayit ekrani yok; panele ilk giris bu hesapla yapilir.
/// Veritabaninda kullanici varsa hicbir sey yapilmaz — mevcut parolalar asla degistirilmez.
/// </summary>
public static class IdentitySeeder
{
    // Karistirilabilecek karakterler (0/O, 1/l/I) uretilen paroladan cikarildi.
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var logger = services.GetRequiredService<ILogger<ApplicationUser>>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();

        if (await users.Users.AnyAsync(ct))
        {
            return;
        }

        var options = services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var generated = string.IsNullOrWhiteSpace(options.AdminPassword);
        var password = generated ? GeneratePassword(options.MinimumPasswordLength + 4) : options.AdminPassword!;

        var user = new ApplicationUser
        {
            UserName = options.AdminEmail,
            Email = options.AdminEmail,
            EmailConfirmed = true,
            DisplayName = options.AdminDisplayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError(
                "Yonetici kullanicisi olusturulamadi: {Errors}",
                string.Join(" | ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));
            return;
        }

        if (generated)
        {
            // Parola yalnizca burada bir kez gorunur; Auth:AdminPassword ile sabitlenebilir.
            logger.LogWarning(
                "Yonetici hesabi olusturuldu. E-posta: {Email} · Gecici parola: {Password} — "
                + "bu parolayi kaydedin, log'a bir daha yazilmayacak.",
                options.AdminEmail,
                password);
        }
        else
        {
            logger.LogInformation("Yonetici hesabi olusturuldu: {Email}", options.AdminEmail);
        }
    }

    /// <summary>Identity kurallarini (buyuk/kucuk harf + rakam) garanti eden rastgele parola.</summary>
    private static string GeneratePassword(int length)
    {
        var alphabet = Lower + Upper + Digits;
        var characters = new char[Math.Max(8, length)];

        characters[0] = Pick(Lower);
        characters[1] = Pick(Upper);
        characters[2] = Pick(Digits);

        for (var i = 3; i < characters.Length; i++)
        {
            characters[i] = Pick(alphabet);
        }

        // Zorunlu karakterlerin bastaki sabit sirasini boz.
        for (var i = characters.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }

        return new string(characters);
    }

    private static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];
}
