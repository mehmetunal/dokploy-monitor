namespace DokployMonitor.Core.Localization;

/// <summary>
/// Bir dil icin tek bir ceviri satiri.
///
/// <see cref="Key"/> kaynak dildeki (Turkce) metnin kendisidir: <c>L["Canli Pano"]</c>.
/// Ceviri bulunamazsa anahtarin kendisi gosterilir, yani eksik ceviri ekrani bozmaz.
/// Satirlar panelden (SuperAdmin) duzenlenebilir; resx dosyasi yoktur.
/// </summary>
public class Translation
{
    /// <summary>Iki harfli dil kodu (or. <c>en</c>, <c>de</c>).</summary>
    public required string Culture { get; set; }

    /// <summary>Kaynak metin = anahtar.</summary>
    public required string Key { get; set; }

    /// <summary>Cevrilmis metin. Bos ise "henuz cevrilmedi" demektir.</summary>
    public string? Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Son duzenleyen kullanicinin e-postasi; tohum kayitlarda bostur.</summary>
    public string? UpdatedBy { get; set; }

    public bool IsTranslated => !string.IsNullOrWhiteSpace(Value);
}
