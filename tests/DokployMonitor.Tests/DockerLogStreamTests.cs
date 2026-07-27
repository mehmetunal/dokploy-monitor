using DokployMonitor.Infrastructure.Docker;

namespace DokployMonitor.Tests;

/// <summary>
/// Docker'in servis logu ucu bazi kurulumlarda yanit govdesini kapatmiyor. Sinirsiz
/// okuma bu durumda istegi HttpClient zaman asimina kadar askida birakiyor ve
/// uretimde 125 sn sonra HTTP 500 uretiyordu. Bu testler okuma sinirini ve iptalin
/// calistigini garanti eder.
/// </summary>
public sealed class DockerLogStreamTests
{
    [Fact]
    public async Task ReadBoundedAsync_reads_the_whole_stream_when_it_ends()
    {
        var payload = "merhaba docker"u8.ToArray();
        using var stream = new MemoryStream(payload);

        var result = await DockerLogStream.ReadBoundedAsync(stream, maxBytes: 1024);

        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task ReadBoundedAsync_stops_at_the_byte_limit_even_if_the_stream_never_ends()
    {
        // Sonsuz akis: kapanmayan chunked yanitin taklidi.
        using var stream = new EndlessStream();

        var result = await DockerLogStream.ReadBoundedAsync(stream, maxBytes: 4096);

        Assert.Equal(4096, result.Length);
    }

    [Fact]
    public async Task ReadBoundedAsync_honours_cancellation_on_a_silent_stream()
    {
        // Sessiz ve kapanmayan akis: yalnizca iptal kurtarir (uretimdeki takilma senaryosu).
        using var stream = new SilentStream();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DockerLogStream.ReadBoundedAsync(stream, maxBytes: 8 * 1024 * 1024, ct: cts.Token));
    }

    /// <summary>Her okumada veri veren, hic bitmeyen akis.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Span.Fill((byte)'x');
            return ValueTask.FromResult(buffer.Length);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)'x', offset, count);
            return count;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Hic veri gondermeyen ve kapanmayan akis (askida kalan istek).</summary>
    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
