using System.Runtime.CompilerServices;
using System.Text;
using DokployMonitor.Core.Abstractions;
using DokployMonitor.Infrastructure.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Infrastructure.Logs;

/// <summary>
/// Build loglarini Dokploy'un log klasorunden okur.
///
/// Neden dosyadan? Dokploy'un `/listen-deployment` WebSocket'i `validateRequest` ile
/// oturum cerezi doguluyor ve x-api-key kabul etmiyor — sunucudan sunucuya baglanamayiz.
/// Log dosyalari zaten ayni makinede (/etc/dokploy/logs) durdugundan klasoru salt-okunur
/// mount edip dogrudan okumak hem daha basit hem de Traefik/HTTP2 WebSocket sorunlarindan bagimsiz.
/// </summary>
public sealed class FileDeploymentLogReader(
    IStringLocalizer<SharedResource> text,
    IOptions<LogOptions> options,
    ILogger<FileDeploymentLogReader> logger) : IDeploymentLogReader
{
    private readonly LogOptions _options = options.Value;

    public async Task<LogReadResult> ReadTailAsync(string? logPath, int maxLines, CancellationToken ct = default)
    {
        if (!TryResolve(logPath, out var resolved, out var reason))
        {
            return new LogReadResult([], 0, false, reason);
        }

        try
        {
            await using var stream = OpenShared(resolved);
            var (lines, offset) = await ReadAllLinesAsync(stream, 0, ct);

            var tail = lines.Count > maxLines
                ? lines.Skip(lines.Count - maxLines).ToList()
                : lines;

            return new LogReadResult(tail, offset, true, null);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Log dosyasi okunamadi: {Path}", resolved);
            return new LogReadResult([], 0, false, text["The log file could not be read."]);
        }
    }

    public async IAsyncEnumerable<LogChunk> StreamAsync(
        string? logPath,
        long fromOffset,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!TryResolve(logPath, out var resolved, out _))
        {
            yield break;
        }

        var offset = fromOffset;

        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<string> lines = [];

            try
            {
                await using var stream = OpenShared(resolved);

                // Dosya kucuduyse (rotate/yeniden olusturma) bastan basla.
                if (stream.Length < offset)
                {
                    offset = 0;
                }

                if (stream.Length > offset)
                {
                    (lines, offset) = await ReadAllLinesAsync(stream, offset, ct);
                }
            }
            catch (IOException)
            {
                // Dosya o an yaziliyor olabilir; bir sonraki turda tekrar deneriz.
            }

            if (lines.Count > 0)
            {
                yield return new LogChunk(lines, offset);
            }

            try
            {
                await Task.Delay(_options.PollIntervalMs, ct);
            }
            catch (TaskCanceledException)
            {
                yield break;
            }
        }
    }

    public async Task<string?> ArchiveAsync(string? logPath, string deploymentId, CancellationToken ct = default)
    {
        if (!TryResolve(logPath, out var resolved, out _))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_options.ArchivePath);
            var target = Path.Combine(_options.ArchivePath, $"{Sanitize(deploymentId)}.log");

            await using var source = OpenShared(resolved);
            await using var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, ct);

            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Log arsivlenemedi: {DeploymentId}", deploymentId);
            return null;
        }
    }

    /// <summary>
    /// Dokploy'un mutlak log yolunu konteyner icindeki mount yoluna cevirir ve
    /// sonucun mount kokunun disina cikmadigini dogrular (path traversal korumasi).
    /// </summary>
    private bool TryResolve(string? logPath, out string resolved, out string? reason)
    {
        resolved = string.Empty;

        if (string.IsNullOrWhiteSpace(logPath))
        {
            reason = text["No log path was recorded for this deployment."];
            return false;
        }

        var mountRoot = Path.GetFullPath(_options.MountPath);
        if (!Directory.Exists(mountRoot))
        {
            reason = $"Log klasoru mount edilmemis ({_options.MountPath}). " +
                     "Dokploy'da bu servise /etc/dokploy/logs -> " + _options.MountPath + " (read-only) mount'u ekleyin.";
            return false;
        }

        var hostRoot = _options.HostPath.TrimEnd('/');
        var relative = logPath.StartsWith(hostRoot, StringComparison.Ordinal)
            ? logPath[hostRoot.Length..].TrimStart('/')
            : logPath.TrimStart('/');

        var candidate = Path.GetFullPath(Path.Combine(mountRoot, relative));

        if (!candidate.StartsWith(mountRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            reason = "Gecersiz log yolu.";
            return false;
        }

        if (!File.Exists(candidate))
        {
            reason = text["Build log file not found. Dokploy may have cleaned it up, or the log folder is not mounted."];
            return false;
        }

        resolved = candidate;
        reason = null;
        return true;
    }

    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// Offset'ten itibaren okur. Yarim kalan son satiri (henuz \n gelmemis) disarida
    /// birakir ve offset'i sadece tam satirlarin sonuna kadar ilerletir; boylece
    /// canli takipte satirlar ikiye bolunmez.
    /// </summary>
    private static async Task<(List<string> Lines, long Offset)> ReadAllLinesAsync(
        FileStream stream,
        long fromOffset,
        CancellationToken ct)
    {
        stream.Seek(fromOffset, SeekOrigin.Begin);

        var length = (int)Math.Min(stream.Length - fromOffset, 8 * 1024 * 1024);
        if (length <= 0)
        {
            return ([], fromOffset);
        }

        var buffer = new byte[length];
        var read = await stream.ReadAtLeastAsync(buffer, length, throwOnEndOfStream: false, ct);

        var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
        if (lastNewline < 0)
        {
            // Henuz tam bir satir yok; offset'i ilerletmiyoruz.
            return ([], fromOffset);
        }

        var text = Encoding.UTF8.GetString(buffer, 0, lastNewline + 1);
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None)
            .SkipLast(1) // son eleman her zaman bos (metin \n ile bitiyor)
            .ToList();

        return (lines, fromOffset + lastNewline + 1);
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
