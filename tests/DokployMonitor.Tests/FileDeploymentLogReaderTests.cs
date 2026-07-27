using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Infrastructure.Logs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Tests;

/// <summary>
/// Log okuyucu, Dokploy'un mutlak yolunu (/etc/dokploy/logs/...) konteynerdeki
/// mount noktasina cevirir ve dosyayi canli takip eder. Kritik davranis: build
/// devam ederken yarim yazilmis son satir ekrana dusmemeli.
/// </summary>
public sealed class FileDeploymentLogReaderTests : IDisposable
{
    private readonly string _mountPath = Path.Combine(Path.GetTempPath(), "dm-logs-" + Guid.NewGuid().ToString("N"));
    private readonly FileDeploymentLogReader _reader;

    public FileDeploymentLogReaderTests()
    {
        Directory.CreateDirectory(Path.Combine(_mountPath, "api"));

        _reader = new FileDeploymentLogReader(
            new SourceLanguageLocalizer(),
            Options.Create(new LogOptions
            {
                MountPath = _mountPath,
                HostPath = "/etc/dokploy/logs",
                ArchivePath = Path.Combine(_mountPath, "archive"),
                PollIntervalMs = 50,
            }),
            NullLogger<FileDeploymentLogReader>.Instance);
    }

    [Fact]
    public async Task ReadTailAsync_maps_host_path_to_mount_and_reads_lines()
    {
        Write("api/build.log", "step 1\nstep 2\nstep 3\n");

        var result = await _reader.ReadTailAsync("/etc/dokploy/logs/api/build.log", maxLines: 10);

        Assert.True(result.Available);
        Assert.Equal(["step 1", "step 2", "step 3"], result.Lines);
        Assert.True(result.Offset > 0);
    }

    [Fact]
    public async Task ReadTailAsync_returns_only_the_last_lines()
    {
        Write("api/build.log", string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line {i}")) + "\n");

        var result = await _reader.ReadTailAsync("/etc/dokploy/logs/api/build.log", maxLines: 5);

        Assert.Equal(5, result.Lines.Count);
        Assert.Equal("line 96", result.Lines[0]);
        Assert.Equal("line 100", result.Lines[4]);
    }

    [Fact]
    public async Task Partial_last_line_is_withheld_until_the_newline_arrives()
    {
        Write("api/build.log", "complete line\npartial");

        var first = await _reader.ReadTailAsync("/etc/dokploy/logs/api/build.log", maxLines: 10);

        // "partial" henuz \n almadi; yarim satir gosterilmemeli.
        Assert.Equal(["complete line"], first.Lines);

        Append("api/build.log", " line now finished\n");

        var second = await _reader.ReadTailAsync("/etc/dokploy/logs/api/build.log", maxLines: 10);
        Assert.Equal(["complete line", "partial line now finished"], second.Lines);
    }

    [Fact]
    public async Task StreamAsync_emits_lines_appended_after_the_offset()
    {
        Write("api/build.log", "first\n");
        var initial = await _reader.ReadTailAsync("/etc/dokploy/logs/api/build.log", maxLines: 10);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var streaming = Task.Run(async () =>
        {
            await foreach (var chunk in _reader.StreamAsync("/etc/dokploy/logs/api/build.log", initial.Offset, cts.Token))
            {
                return chunk.Lines;
            }

            return [];
        }, cts.Token);

        await Task.Delay(120, cts.Token);
        Append("api/build.log", "second\nthird\n");

        var lines = await streaming;
        Assert.Equal(["second", "third"], lines);
    }

    [Fact]
    public async Task Unknown_paths_are_reported_instead_of_throwing()
    {
        var missing = await _reader.ReadTailAsync("/etc/dokploy/logs/api/nope.log", maxLines: 10);
        Assert.False(missing.Available);
        Assert.NotNull(missing.UnavailableReason);

        var blank = await _reader.ReadTailAsync(null, maxLines: 10);
        Assert.False(blank.Available);
    }

    /// <summary>
    /// Dosya yoksa sebep ayirt edilmeli: klasor var ama dosya yok ise mevcut dosya
    /// adlari yazilir (isim uyusmazligi aninda gorunur).
    /// </summary>
    [Fact]
    public async Task Missing_file_in_an_existing_folder_lists_the_files_that_are_there()
    {
        Write("api/baska.log", "x\n");

        var result = await _reader.ReadTailAsync("/etc/dokploy/logs/api/nope.log", maxLines: 10);

        Assert.False(result.Available);
        Assert.Contains("nope.log", result.UnavailableReason);
        Assert.Contains("baska.log", result.UnavailableReason);
    }

    /// <summary>Servisin klasoru hic yoksa: deployment baska sunucuda kosmus olabilir.</summary>
    [Fact]
    public async Task Missing_service_folder_points_at_another_server_or_cleanup()
    {
        Write("api/build.log", "x\n");   // mount bos olmasin

        var result = await _reader.ReadTailAsync("/etc/dokploy/logs/baska-servis/build.log", maxLines: 10);

        Assert.False(result.Available);
        Assert.Contains("another Dokploy server", result.UnavailableReason);
    }

    /// <summary>Mount noktasi bos: bind mount yanlis klasoru gosteriyor.</summary>
    [Fact]
    public async Task Empty_mount_point_is_reported_as_a_wrong_bind_mount()
    {
        var empty = Path.Combine(Path.GetTempPath(), "dm-logs-bos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);

        var reader = new FileDeploymentLogReader(
            new SourceLanguageLocalizer(),
            Options.Create(new LogOptions { MountPath = empty, HostPath = "/etc/dokploy/logs" }),
            NullLogger<FileDeploymentLogReader>.Instance);

        var result = await reader.ReadTailAsync("/etc/dokploy/logs/api/build.log", maxLines: 10);

        Assert.False(result.Available);
        Assert.Contains("empty", result.UnavailableReason);
        Assert.Contains("/etc/dokploy/logs", result.UnavailableReason);

        Directory.Delete(empty, recursive: true);
    }

    /// <summary>Servis klasoru var ama tamamen bos: log temizlenmis.</summary>
    [Fact]
    public async Task Empty_service_folder_is_reported_as_a_cleaned_log()
    {
        Directory.CreateDirectory(Path.Combine(_mountPath, "web"));

        var result = await _reader.ReadTailAsync("/etc/dokploy/logs/web/build.log", maxLines: 10);

        Assert.False(result.Available);
        Assert.Contains("cleaned the log", result.UnavailableReason);
    }

    /// <summary>Mount klasoru hic yoksa mesaj mount'un nasil eklenecegini soylemeli.</summary>
    [Fact]
    public async Task Missing_mount_is_reported_with_the_expected_mount_pair()
    {
        var reader = new FileDeploymentLogReader(
            new SourceLanguageLocalizer(),
            Options.Create(new LogOptions
            {
                MountPath = Path.Combine(Path.GetTempPath(), "dm-logs-yok-" + Guid.NewGuid().ToString("N")),
                HostPath = "/etc/dokploy/logs",
            }),
            NullLogger<FileDeploymentLogReader>.Instance);

        var result = await reader.ReadTailAsync("/etc/dokploy/logs/api/build.log", maxLines: 10);

        Assert.False(result.Available);
        Assert.Contains("/etc/dokploy/logs", result.UnavailableReason);
        Assert.Contains("not mounted", result.UnavailableReason);
    }

    [Fact]
    public async Task Paths_escaping_the_mount_root_are_rejected()
    {
        // Dokploy'dan gelen yol bozuk/kotucul olsa bile mount kokunun disina cikilmamali.
        var result = await _reader.ReadTailAsync("/etc/dokploy/logs/../../../etc/passwd", maxLines: 10);

        Assert.False(result.Available);
    }

    [Fact]
    public async Task ArchiveAsync_copies_the_log_for_later_inspection()
    {
        Write("api/build.log", "failure details\n");

        var archived = await _reader.ArchiveAsync("/etc/dokploy/logs/api/build.log", "dep-42");

        Assert.NotNull(archived);
        Assert.True(File.Exists(archived));
        Assert.Contains("failure details", await File.ReadAllTextAsync(archived));
    }

    private void Write(string relative, string content) =>
        File.WriteAllText(Path.Combine(_mountPath, relative), content);

    private void Append(string relative, string content) =>
        File.AppendAllText(Path.Combine(_mountPath, relative), content);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_mountPath, recursive: true);
        }
        catch (IOException)
        {
            // Gecici klasor temizligi testleri etkilemesin.
        }
    }
}
