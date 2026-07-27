namespace DokployMonitor.Infrastructure.Docker;

/// <summary>
/// Docker Engine API'sinin log govdesini **sinirli** okur.
///
/// Nicin gerekli: swarm servis logu ucu (<c>/services/{ad}/logs</c>) bazi kurulumlarda
/// yanit govdesini kapatmaz — chunked akis surekli acik kalir. Sinirsiz bir
/// <c>CopyToAsync</c> bu durumda HttpClient zaman asimina kadar bekler ve istek
/// iptal istisnasiyla duser (uretimde 125 sn sonra HTTP 500 olarak gorulmustu).
///
/// Bu yuzden okuma iki sekilde sinirlanir: en fazla <c>maxBytes</c> bayt okunur ve
/// iptal jetonu (cagiran tarafta kisa bir sure bahsi) her an araya girebilir.
/// Elde edilen bayt bloklari, cerceve cozumlemesi icin <see cref="DockerLogFrames"/>'e verilir.
/// </summary>
public static class DockerLogStream
{
    /// <summary>Tek istekte okunacak en fazla govde boyutu (8 MB).</summary>
    public const int DefaultMaxBytes = 8 * 1024 * 1024;

    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// <paramref name="stream"/>'i en fazla <paramref name="maxBytes"/> bayt okur.
    /// Sinira ulasilinca akis kapanmasa bile okuma durur; iptal edilirse
    /// <see cref="OperationCanceledException"/> firlatir (cagiran karar verir).
    /// </summary>
    public static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maxBytes = DefaultMaxBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);

        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];

        while (buffer.Length < maxBytes)
        {
            var wanted = (int)Math.Min(chunk.Length, maxBytes - buffer.Length);
            var read = await stream.ReadAsync(chunk.AsMemory(0, wanted), ct);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
