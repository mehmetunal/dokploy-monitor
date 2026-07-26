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
