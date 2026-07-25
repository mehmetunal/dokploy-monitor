using System.Net;
using System.Text;
using System.Text.Json;
using DokployMonitor.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Infrastructure.Docker;

/// <summary>
/// Container loglarini Docker Engine API'sinden okur (unix soketi uzerinden).
///
/// Dokploy uygulamalari Swarm servisi olarak calisir, bu yuzden once
/// <c>/services/{ad}/logs</c> denenir; bulunamazsa ayni adli container icin
/// <c>/containers/{ad}/logs</c> denenir (compose servisleri ve tek container'lar icin).
///
/// Yanit "multiplexed" akistir: her karenin basinda 8 byte'lik baslik bulunur
/// (1 byte akis turu, 3 byte dolgu, 4 byte big-endian uzunluk). TTY'li container'larda
/// baslik yoktur; bu durumda icerik ham metin olarak okunur.
/// </summary>
public sealed class DockerLogReader(
    IHttpClientFactory httpClientFactory,
    IOptions<DockerOptions> options,
    ILogger<DockerLogReader> logger) : IContainerLogReader
{
    public const string HttpClientName = "docker-engine";

    private readonly DockerOptions _options = options.Value;

    public async Task<LogReadResult> ReadTailAsync(
        string? serviceOrContainerName,
        int maxLines,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Unavailable("Container logu kapali (Docker:Enabled = false).");
        }

        if (string.IsNullOrWhiteSpace(serviceOrContainerName))
        {
            return Unavailable("Bu deployment icin servis/container adi bilinmiyor.");
        }

        if (!File.Exists(_options.SocketPath))
        {
            return Unavailable(
                $"Docker soketi bulunamadi ({_options.SocketPath}). Konteynere "
                + "/var/run/docker.sock:/var/run/docker.sock:ro mount'u ekleyin.");
        }

        var tail = Math.Clamp(maxLines, 1, _options.MaxTailLines);
        var name = Uri.EscapeDataString(serviceOrContainerName);
        var query = $"stdout=1&stderr=1&tail={tail}";

        // Swarm servisi once: Dokploy uygulamalari swarm servisi olarak calisir. Bu deneme
        // "yumusak": swarm kapaliysa daemon 503 doner, servis yoksa 404 — her iki durumda da
        // ayni adli container denenir.
        var service = await TryReadAsync($"services/{name}/logs?{query}&details=0", ct);
        if (service.Available)
        {
            return service.Result;
        }

        var container = await TryReadAsync($"containers/{name}/logs?{query}", ct);
        if (container.Available)
        {
            return container.Result;
        }

        // Ikisi de olmadi: container denemesinin sebebi daha aciklayicidir (404 = yok).
        return container.NotFound && service.NotFound
            ? Unavailable(
                $"Docker'da '{serviceOrContainerName}' adli servis veya container bulunamadi. "
                + "Servis silinmis olabilir ya da Monitor baska bir sunucuda calisiyor.")
            : container.Result;
    }

    public async Task<ContainerLogHealth> CheckHealthAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return new ContainerLogHealth(false, false, false, null, "Docker:Enabled = false");
        }

        var socketExists = File.Exists(_options.SocketPath);
        if (!socketExists)
        {
            return new ContainerLogHealth(
                true, false, false, null,
                $"Soket dosyasi yok: {_options.SocketPath}");
        }

        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync($"{_options.ApiVersion}/version", ct);

            if (!response.IsSuccessStatusCode)
            {
                return new ContainerLogHealth(
                    true, true, false, null,
                    $"Engine API {(int)response.StatusCode} dondu.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var version = document.RootElement.TryGetProperty("Version", out var value) ? value.GetString() : null;

            return new ContainerLogHealth(true, true, true, version, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return new ContainerLogHealth(true, true, false, null, ex.Message);
        }
    }

    private async Task<(bool Available, bool NotFound, LogReadResult Result)> TryReadAsync(
        string path,
        CancellationToken ct)
    {
        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(
                $"{_options.ApiVersion}/{path}",
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                // 503 = daemon swarm yoneticisi degil; 404 = boyle bir servis/container yok.
                var reason = $"Docker Engine API {(int)response.StatusCode} dondu ({path.Split('?')[0]}).";
                logger.LogDebug("Container logu okunamadi: {Reason}", reason);

                var notFound = response.StatusCode is HttpStatusCode.NotFound
                    or HttpStatusCode.ServiceUnavailable;

                return (false, notFound, Unavailable(reason));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var lines = await ReadStreamAsync(stream, ct);

            return (true, false, new LogReadResult(lines, 0, true, null));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            var reason = $"Docker soketine baglanilamadi: {ex.Message}";
            logger.LogWarning(ex, "Container logu okunamadi.");
            return (false, false, Unavailable(reason));
        }
    }

    private static async Task<List<string>> ReadStreamAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);

        return DockerLogFrames.ToLines(buffer.ToArray());
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 2, 120));
        return client;
    }

    private static LogReadResult Unavailable(string reason) => new([], 0, false, reason);
}
