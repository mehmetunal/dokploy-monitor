using System.Globalization;
using System.Text.Json;

namespace DokployMonitor.Infrastructure.Dokploy;

/// <summary>
/// Dokploy'un OpenAPI dokumaninda yanit govdeleri bos tanimli ("{}"), yani alanlari
/// derleme zamaninda garanti edemiyoruz. Bu yuzden yanitlari POCO'ya deserialize etmek
/// yerine JsonElement uzerinden savunmaci okuyoruz: eksik/degisen alan istisna firlatmaz.
///
/// Tum yardimcilar <see cref="Nullable{JsonElement}"/> uzerinde calisir; boylece
/// element.Prop("application").Prop("environment").Str("name") gibi zincirler
/// ara adimlar bos olsa bile guvenle yazilabilir.
/// </summary>
internal static class JsonElementExtensions
{
    // C# genisletme yontemlerinde alici (receiver) icin JsonElement -> JsonElement?
    // ortuk donusumu uygulanmaz; bu yuzden her yardimcinin non-nullable karsiligi da var.
    public static JsonElement? Prop(this JsonElement element, string name) => ((JsonElement?)element).Prop(name);

    public static string? Str(this JsonElement element, string name) => ((JsonElement?)element).Str(name);

    public static bool Bool(this JsonElement element, string name, bool fallback = false) =>
        ((JsonElement?)element).Bool(name, fallback);

    public static DateTimeOffset? Date(this JsonElement element, string name) => ((JsonElement?)element).Date(name);

    public static IReadOnlyList<JsonElement> AsArray(this JsonElement element) => ((JsonElement?)element).AsArray();

    public static JsonElement? Prop(this JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } obj)
        {
            return null;
        }

        return obj.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
    }

    public static string? Str(this JsonElement? element, string name)
    {
        var prop = element.Prop(name);
        return prop?.ValueKind switch
        {
            JsonValueKind.String => prop.Value.GetString(),
            JsonValueKind.Number => prop.Value.ToString(),
            _ => null,
        };
    }

    public static bool Bool(this JsonElement? element, string name, bool fallback = false)
    {
        var prop = element.Prop(name);
        return prop?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }

    public static DateTimeOffset? Date(this JsonElement? element, string name)
    {
        if (element.Prop(name) is not { } prop)
        {
            return null;
        }

        // Dokploy tarihleri ISO-8601 metin olarak tutuyor; kuyruk API'si ise epoch ms doner.
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var epochMs))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
        }

        var text = prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Yaniti bir dizi olarak yorumlar. Kok bir dizi olabilecegi gibi,
    /// {"result": [...]} / {"data": [...]} gibi sarmalanmis da olabilir.
    /// </summary>
    public static IReadOnlyList<JsonElement> AsArray(this JsonElement? element)
    {
        if (element is not { } root)
        {
            return [];
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            return [.. root.EnumerateArray()];
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var wrapper in (ReadOnlySpan<string>)["result", "data", "items", "json"])
            {
                if (!root.TryGetProperty(wrapper, out var inner))
                {
                    continue;
                }

                if (inner.ValueKind == JsonValueKind.Array)
                {
                    return [.. inner.EnumerateArray()];
                }

                // tRPC bazen {"result":{"data":[...]}} seklinde ic ice sarmalar.
                if (inner.ValueKind == JsonValueKind.Object)
                {
                    var nested = ((JsonElement?)inner).AsArray();
                    if (nested.Count > 0)
                    {
                        return nested;
                    }
                }
            }
        }

        return [];
    }
}
