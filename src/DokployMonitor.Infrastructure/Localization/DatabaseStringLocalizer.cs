using System.Globalization;
using Microsoft.Extensions.Localization;

namespace DokployMonitor.Infrastructure.Localization;

/// <summary>
/// Cevirileri veritabanindan (bellek ici anlik goruntu uzerinden) okuyan localizer.
///
/// Anahtar kaynak dildeki metnin kendisidir; ceviri yoksa anahtar dondurulur ve
/// <see cref="LocalizedString.ResourceNotFound"/> true olur — ekran bozulmaz.
/// Kultur her cagride <see cref="CultureInfo.CurrentUICulture"/>'dan okunur, bu yuzden
/// tek bir ornek tum diller icin kullanilabilir (singleton).
/// </summary>
public sealed class DatabaseStringLocalizer(TranslationStore store) : IStringLocalizer
{
    public LocalizedString this[string name] => Build(name, null);

    public LocalizedString this[string name, params object[] arguments] => Build(name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        store.All(CurrentCulture())
            .Select(pair => new LocalizedString(pair.Key, pair.Value, resourceNotFound: false));

    private LocalizedString Build(string name, object[]? arguments)
    {
        var translation = store.Find(CurrentCulture(), name);
        var text = translation ?? name;

        if (arguments is { Length: > 0 })
        {
            text = string.Format(CultureInfo.CurrentCulture, text, arguments);
        }

        return new LocalizedString(name, text, resourceNotFound: translation is null);
    }

    private static string CurrentCulture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
}

/// <summary>Tum kaynak tipleri icin ayni veritabani localizer'ini doner.</summary>
public sealed class DatabaseStringLocalizerFactory(TranslationStore store) : IStringLocalizerFactory
{
    private readonly DatabaseStringLocalizer _localizer = new(store);

    public IStringLocalizer Create(Type resourceSource) => _localizer;

    public IStringLocalizer Create(string baseName, string location) => _localizer;
}

/// <summary>Genel tipli enjeksiyon (<c>IStringLocalizer&lt;SharedResource&gt;</c>) icin sarmalayici.</summary>
public sealed class DatabaseStringLocalizer<T>(TranslationStore store)
    : DatabaseStringLocalizerWrapper(store), IStringLocalizer<T>
{
}

public abstract class DatabaseStringLocalizerWrapper(TranslationStore store) : IStringLocalizer
{
    private readonly DatabaseStringLocalizer _inner = new(store);

    public LocalizedString this[string name] => _inner[name];

    public LocalizedString this[string name, params object[] arguments] => _inner[name, arguments];

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        _inner.GetAllStrings(includeParentCultures);
}
