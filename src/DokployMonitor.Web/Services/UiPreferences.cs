namespace DokployMonitor.Web.Services;

/// <summary>
/// Tema tercihi. <see cref="System"/> tarayicinin/isletim sisteminin tercihini kullanir
/// (`prefers-color-scheme`), digerleri sabittir.
/// </summary>
public enum ThemePreference
{
    System = 0,
    Dark = 1,
    Light = 2,
}

/// <summary>
/// Kullanici arayuzu tercihleri (tema, dil) icin cerez adlari ve okuma yardimcilari.
///
/// Tema sunucu tarafinda okunur ve <c>&lt;html data-bs-theme&gt;</c> ilk render'da
/// yazilir; boylece sayfa acilirken yanlis temayla "flash" olmaz.
/// </summary>
public static class UiPreferences
{
    public const string ThemeCookie = "dm.theme";

    /// <summary>ASP.NET Core kultur cerezinin standart adi (RequestLocalization ile ayni).</summary>
    public const string CultureCookie = ".AspNetCore.Culture";

    public static ThemePreference ReadTheme(HttpRequest request) =>
        request.Cookies.TryGetValue(ThemeCookie, out var value)
        && Enum.TryParse<ThemePreference>(value, ignoreCase: true, out var parsed)
            ? parsed
            : ThemePreference.System;

    /// <summary>
    /// `data-bs-theme` degeri. Sistem tercihinde sunucu tarayicinin tercihini bilemez;
    /// koyu ile baslanir ve istemci tarafi <c>site.js</c> gerekirse aydinliga cevirir.
    /// </summary>
    public static string ToBootstrapTheme(ThemePreference preference) =>
        preference switch
        {
            ThemePreference.Light => "light",
            ThemePreference.Dark => "dark",
            _ => "dark",
        };
}
