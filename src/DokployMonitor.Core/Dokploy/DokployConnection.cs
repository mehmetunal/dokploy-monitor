namespace DokployMonitor.Core.Dokploy;

/// <summary>
/// Izlenen bir Dokploy sunucusu (bir API anahtari = bir baglanti).
///
/// Birden fazla kayit desteklenir: her baglanti ayri sunucu ya da ayni sunucuda ayri
/// organizasyon olabilir. Senkronizasyon isciler tum <see cref="Enabled"/> baglantilari
/// dolasir ve toplanan deployment kayitlarini <see cref="Id"/> ile etiketler.
/// </summary>
public class DokployConnection
{
    public required string Id { get; set; }

    /// <summary>Ekranlarda gorunen kisa ad (or. "Uretim", "Ege Tatil Evleri").</summary>
    public required string Name { get; set; }

    /// <summary>Panel koku, /api olmadan.</summary>
    public required string BaseUrl { get; set; }

    /// <summary>x-api-key degeri. Veritabaninda duz metin tutulur; DB dosyasi korunmalidir.</summary>
    public required string ApiKey { get; set; }

    public bool Enabled { get; set; } = true;

    public bool AllowInvalidCertificates { get; set; }

    public bool ForceLegacyDiscovery { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Legacy modda es zamanli istek siniri.</summary>
    public int MaxParallelRequests { get; set; } = 4;

    public DateTimeOffset CreatedAt { get; set; }

    // --- Son senkronizasyon durumu (tanilama ve baglanti listesi icin) ---
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? LastSyncError { get; set; }

    /// <summary>Istek gonderilecek kok adres: <c>{BaseUrl}/api/</c></summary>
    public Uri ApiBaseUri() => new($"{BaseUrl.TrimEnd('/')}/api/", UriKind.Absolute);

    /// <summary>Kayitli bir API anahtari var mi? (Ekranda "kayitli" isareti icin.)</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// Anahtarin ekranda gosterilebilir maskeli hali; anahtar yoksa bos doner.
    /// "Tanimli degil" metnini gorunum yazar, cunku metinler yerelleştirilmelidir.
    /// </summary>
    public string MaskedApiKey =>
        !HasApiKey
            ? string.Empty
            : ApiKey.Length <= 12 ? "••••" : $"{ApiKey[..8]}…{ApiKey[^4..]}";
}
