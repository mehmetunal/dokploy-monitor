namespace DokployMonitor.Core.Abstractions;

/// <summary>
/// Deployment build loglarini okur.
/// Dokploy'un `/listen-deployment` WebSocket'i oturum cerezi istedigi ve API anahtarini
/// kabul etmedigi icin loglari dosya sisteminden okuyoruz: Dokploy'un log klasoru
/// (/etc/dokploy/logs) konteynere salt-okunur mount edilir.
/// </summary>
public interface IDeploymentLogReader
{
    /// <summary>Log dosyasinin son <paramref name="maxLines"/> satirini doner.</summary>
    Task<LogReadResult> ReadTailAsync(string? logPath, int maxLines, CancellationToken ct = default);

    /// <summary>
    /// Dosyayi <paramref name="fromOffset"/> byte'indan itibaren canli takip eder
    /// (tail -f davranisi). Dosya buyudukce yeni satirlari akitir.
    /// </summary>
    IAsyncEnumerable<LogChunk> StreamAsync(string? logPath, long fromOffset, CancellationToken ct = default);

    /// <summary>Hatali bir deployment'in logunu kalici arsive kopyalar; arsiv yolunu doner.</summary>
    Task<string?> ArchiveAsync(string? logPath, string deploymentId, CancellationToken ct = default);
}

public sealed record LogReadResult(
    IReadOnlyList<string> Lines,
    long Offset,
    bool Available,
    string? UnavailableReason);

public sealed record LogChunk(IReadOnlyList<string> Lines, long Offset);
