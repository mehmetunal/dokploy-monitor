using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DokployMonitor.Core.Abstractions;
using DokployMonitor.Infrastructure.Localization;
using Microsoft.Extensions.Localization;
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
    IStringLocalizer<SharedResource> text,
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
            return Unavailable(text["Container logs are disabled (Docker:Enabled = false)."]);
        }

        if (string.IsNullOrWhiteSpace(serviceOrContainerName))
        {
            return Unavailable(text["The service/container name for this deployment is unknown."]);
        }

        if (!File.Exists(_options.SocketPath))
        {
            return Unavailable(text["Docker socket not found ({0}). Add the mount /var/run/docker.sock:/var/run/docker.sock:ro to the container.", _options.SocketPath]);
        }

        var tail = Math.Clamp(maxLines, 1, _options.MaxTailLines);
        var name = Uri.EscapeDataString(serviceOrContainerName);

        // follow=0 acikca yazilir: bazi daemon/istemci bilesimlerinde servis logu ucu
        // yanit govdesini kapatmiyor ve istek zaman asimina kadar askida kaliyor.
        var query = $"stdout=1&stderr=1&follow=0&tail={tail}";

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
            ? Unavailable(text["No Docker service or container named '{0}'. It may have been removed, or Monitor runs on a different host.", serviceOrContainerName])
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
                text["The socket file does not exist: {0}", _options.SocketPath]);
        }

        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync($"{_options.ApiVersion}/version", ct);

            if (!response.IsSuccessStatusCode)
            {
                return new ContainerLogHealth(
                    true, true, false, null,
                    text["The Docker Engine API returned {0}.", (int)response.StatusCode]);
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
        // Log kuyrugu okumak saniyeler surer. Akis kapanmazsa (bkz. DockerLogStream)
        // istegi HttpClient zaman asimina birakmak yerine kendi butcemizle keseriz;
        // boylece kullaniciya 2 dakika sonra 500 degil, aninda anlasilir bir mesaj doner.
        //
        // Ust sinir 15 sn: Docker__TimeoutSeconds daha buyuk verilse bile ekran istegi
        // bunu asmaz (en kotu durum servis + container denemesi = 2 x butce).
        var budget = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 2, 15));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(budget);

        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(
                $"{_options.ApiVersion}/{path}",
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                // 503 = daemon swarm yoneticisi degil; 404 = boyle bir servis/container yok.
                var reason = text["The Docker Engine API returned {0} ({1}).",
                    (int)response.StatusCode, path.Split('?')[0]];
                logger.LogDebug("Container logu okunamadi: {Reason}", reason);

                var notFound = response.StatusCode is HttpStatusCode.NotFound
                    or HttpStatusCode.ServiceUnavailable;

                return (false, notFound, Unavailable(reason));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var payload = await DockerLogStream.ReadBoundedAsync(stream, ct: timeout.Token);

            return (true, false, new LogReadResult(DockerLogFrames.ToLines(payload), 0, true, null));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Butce doldu: akis kapanmiyor ya da daemon yavas. Istek basarisiz degil,
            // yalnizca container logu alinamadi — cagiran build loguna dusebilir.
            var reason = text["Timed out after {0} s while reading the container log. The service may be streaming continuously; use the build log instead.",
                (int)budget.TotalSeconds];

            logger.LogWarning("Container logu zaman asimina ugradi ({Budget}s): {Path}", budget.TotalSeconds, path);
            return (false, false, Unavailable(reason));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            var reason = IsAccessDenied(ex)
                ? text["Permission denied on the Docker socket ({0}). Add the socket group (e.g. the docker GID) to the container; temporary workaround: chmod 666 {0}.", _options.SocketPath]
                : text["Could not connect to the Docker socket: {0}", ex.Message];

            // AccessDenied beklenen bir yapilandirma sorunu; stack trace gurultu yaratmasin.
            logger.LogWarning("Container logu okunamadi: {Reason}", reason);
            return (false, false, Unavailable(reason));
        }
    }

    private static bool IsAccessDenied(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.AccessDenied })
            {
                return true;
            }

            if (current.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 2, 120));
        return client;
    }

    private static LogReadResult Unavailable(string reason) => new([], 0, false, reason);
}
