using Microsoft.AspNetCore.Identity;

namespace DokployMonitor.Infrastructure.Identity;

/// <summary>
/// Panele giris yapan kullanici. Kayit ekrani yok: ilk yonetici acilista
/// <c>Auth</c> yapilandirmasindan olusturulur, sonrasi elle yonetilir.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    /// <summary>Ekranda gosterilecek ad; bos ise e-posta kullanilir.</summary>
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? (Email ?? UserName ?? Id) : DisplayName!;
}
