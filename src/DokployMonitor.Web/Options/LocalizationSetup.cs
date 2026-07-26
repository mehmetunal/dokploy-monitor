using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace DokployMonitor.Web.Options;

/// <param name="Code">Iki harfli dil kodu; ceviri tablosundaki <c>Culture</c> ile ayni.</param>
/// <param name="NativeName">Dil secicide gorunen, kendi dilindeki ad.</param>
/// <param name="Flag">Flag emoji shown next to the language name in the picker.</param>
/// <param name="RightToLeft">Right-to-left languages (Arabic, Persian, Hebrew...).</param>
public sealed record SupportedCulture(string Code, string NativeName, string Flag, bool RightToLeft = false);

/// <summary>
/// Desteklenen diller ve secim sirasi.
///
/// Source language is English: in views the key **is the English text** (<c>L["Connections"]</c>).
/// Ceviriler veritabanindaki <c>Translations</c> tablosunda tutulur ve panelden duzenlenir;
/// ceviri bulunamazsa anahtar (Turkce metin) gosterilir — eksik ceviri sayfayi bozmaz.
///
/// Kodlar iki harfli tutulur: istek <c>zh-Hans</c> / <c>pt-BR</c> gibi gelse de
/// <see cref="CultureInfo.TwoLetterISOLanguageName"/> ile ayni satira duser.
/// </summary>
public static class LocalizationSetup
{
    /// <summary>Source language; keys are English texts, so no translation rows are needed.</summary>
    public const string DefaultCulture = "en";

    /// <summary>Yeni dil eklemek icin buraya bir satir yeterli (ceviriler panelden girilebilir).</summary>
    public static readonly SupportedCulture[] Supported =
    [
        new("en", "English", "🇬🇧"),
        new("tr", "Türkçe", "🇹🇷"),
        new("de", "Deutsch", "🇩🇪"),
        new("fr", "Français", "🇫🇷"),
        new("es", "Español", "🇪🇸"),
        new("pt", "Português", "🇵🇹"),
        new("it", "Italiano", "🇮🇹"),
        new("nl", "Nederlands", "🇳🇱"),
        new("pl", "Polski", "🇵🇱"),
        new("ru", "Русский", "🇷🇺"),
        new("uk", "Українська", "🇺🇦"),
        new("ar", "العربية", "🇸🇦", RightToLeft: true),
        new("zh", "简体中文", "🇨🇳"),
        new("ja", "日本語", "🇯🇵"),
        new("ko", "한국어", "🇰🇷"),
        new("hi", "हिन्दी", "🇮🇳"),
        new("id", "Bahasa Indonesia", "🇮🇩"),
    ];

    /// <summary>Geriye uyumlu kisayol: (kod, ad) ciftleri.</summary>
    public static IReadOnlyList<(string Code, string NativeName)> SupportedCultures { get; } =
        [.. Supported.Select(culture => (culture.Code, culture.NativeName))];

    public static RequestLocalizationOptions Build()
    {
        var cultures = Supported.Select(culture => new CultureInfo(culture.Code)).ToList();

        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(DefaultCulture),
            SupportedCultures = cultures,
            SupportedUICultures = cultures,
            ApplyCurrentCultureToResponseHeaders = true,
        };

        // Sira onemli: once kullanicinin acik secimi (cerez), sonra tarayici/sistem dili.
        options.RequestCultureProviders =
        [
            new CookieRequestCultureProvider(),
            new AcceptLanguageHeaderRequestCultureProvider(),
        ];

        return options;
    }

    public static bool IsSupported(string? code) =>
        Supported.Any(culture => string.Equals(culture.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Flag emoji for a culture code; empty when unknown.</summary>
    public static string Flag(string code) =>
        Supported.FirstOrDefault(culture =>
            string.Equals(culture.Code, code, StringComparison.OrdinalIgnoreCase))?.Flag ?? string.Empty;

    /// <summary>Display name for a culture code (falls back to the code itself).</summary>
    public static string NativeName(string code) =>
        Supported.FirstOrDefault(culture =>
            string.Equals(culture.Code, code, StringComparison.OrdinalIgnoreCase))?.NativeName ?? code;

    /// <summary>Sagdan sola diller icin <c>&lt;html dir="rtl"&gt;</c> gerekir.</summary>
    public static bool IsRightToLeft(string code) =>
        Supported.FirstOrDefault(culture =>
            string.Equals(culture.Code, code, StringComparison.OrdinalIgnoreCase))?.RightToLeft ?? false;
}
