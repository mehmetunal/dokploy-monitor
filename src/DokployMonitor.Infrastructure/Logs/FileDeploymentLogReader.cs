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
            reason = text["The log folder is not mounted ({0}). Add the mount {1} -> {0} (read-only) to this service in Dokploy.",
                _options.MountPath, _options.HostPath];
            return false;
        }

        var hostRoot = _options.HostPath.TrimEnd('/');
        var relative = logPath.StartsWith(hostRoot, StringComparison.Ordinal)
            ? logPath[hostRoot.Length..].TrimStart('/')
            : logPath.TrimStart('/');

        var candidate = Path.GetFullPath(Path.Combine(mountRoot, relative));

        if (!candidate.StartsWith(mountRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            reason = text["Invalid log path."];
            return false;
        }

        if (!File.Exists(candidate))
        {
            logger.LogDebug("Build log bulunamadi. Denenen yol: {Candidate} (kayitli: {LogPath})", candidate, logPath);
            reason = DescribeMissingFile(mountRoot, candidate);
            return false;
        }

        resolved = candidate;
        reason = null;
        return true;
    }

    /// <summary>
    /// Dosya yoksa **nicin** yok sorusunu ayirt eder. Tek bir "bulunamadi" mesaji
    /// uretimde uc farkli sebebi ayni kefeye koyuyordu: yanlis mount, baska sunucuda
    /// kosan deployment ve dosya izinleri. Klasor durumuna bakip her biri icin ayri
    /// mesaj dondururuz; dizindeki ornek dosya adlari da isim uyusmazligini aninda gosterir.
    /// </summary>
    private string DescribeMissingFile(string mountRoot, string candidate)
    {
        var directory = Path.GetDirectoryName(candidate) ?? mountRoot;

        try
        {
            // 1) Mount noktasi var ama bos: bind mount yanlis klasoru gosteriyor.
            if (!Directory.EnumerateFileSystemEntries(mountRoot).Any())
            {
                return text["The log mount point is empty ({0}). The bind mount probably points at the wrong folder; it should be {1} on the host.",
                    _options.MountPath, _options.HostPath];
            }

            // 2) Servisin klasoru hic yok: deployment baska sunucuda kosmus ya da temizlenmis.
            if (!Directory.Exists(directory))
            {
                return text["This service has no log folder in the mount ({0}). The deployment probably ran on another Dokploy server, or Dokploy already cleaned the logs. Use the container log instead.",
                    directory];
            }

            // 3) Klasor var, dosya yok: rotasyon/temizlik. Ornek adlar isim farkini gosterir.
            var samples = Directory.EnumerateFiles(directory)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .Take(3)
                .ToList();

            return samples.Count == 0
                ? text["The service log folder is empty ({0}). Dokploy has probably cleaned the log.", directory]
                : text["Build log file not found ({0}). Dokploy may have rotated or cleaned it. Files in the folder: {1}",
                    Path.GetFileName(candidate), string.Join(", ", samples)];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // File.Exists izin hatasinda da false doner; bu yuzden ayrica raporlanmali.
            logger.LogWarning(ex, "Log klasoru okunamadi: {Directory}", directory);

            return text["The log folder cannot be read ({0}): {1}. The container runs as a non-root user; make the mounted folder readable for it.",
                directory, ex.Message];
        }
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
