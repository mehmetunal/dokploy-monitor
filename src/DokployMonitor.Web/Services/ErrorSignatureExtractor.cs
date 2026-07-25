using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DokployMonitor.Web.Services;

/// <summary>
/// Hata mesajlarini gruplanabilir bir imzaya indirger: ANSI kodlari, GUID'ler,
/// hash'ler, sayilar, dosya yollari ve zaman damgalari degisken oldugu icin
/// temizlenir. Boylece "ayni hata 12 serviste tekrar ediyor" gorunumu uretilebilir.
/// </summary>
public static partial class ErrorSignatureExtractor
{
    public sealed record Signature(string Hash, string NormalizedMessage, string SampleMessage);

    public static Signature? Extract(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return null;
        }

        var sample = errorMessage.Trim();
        var firstLine = AnsiPattern().Replace(sample, string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return null;
        }

        var normalized = Normalize(firstLine);
        if (normalized.Length == 0)
        {
            return null;
        }

        return new Signature(Hash(normalized), normalized, Truncate(sample, 4000));
    }

    public static string Normalize(string line)
    {
        var text = AnsiPattern().Replace(line, string.Empty);
        text = GuidPattern().Replace(text, "{id}");
        text = TimestampPattern().Replace(text, "{ts}");
        text = HexPattern().Replace(text, "{hex}");
        text = PathPattern().Replace(text, "{path}");
        text = NumberPattern().Replace(text, "{n}");
        text = WhitespacePattern().Replace(text, " ");

        return Truncate(text.Trim(), 300);
    }

    private static string Hash(string normalized)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(bytes.AsSpan(0, 16));
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : string.Concat(value.AsSpan(0, max), "...");

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(\.\d+)?Z?")]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"\b[0-9a-f]{12,}\b")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"(/[\w.\-]+){2,}")]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
