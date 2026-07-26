using System.Net.Http.Headers;
using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.Dokploy;
using Microsoft.Extensions.Logging;

namespace DokployMonitor.Infrastructure.Dokploy;

/// <summary>
/// Baglanti basina <see cref="DokployApiClient"/> uretir.
///
/// Sertifika dogrulamasi handler seviyesinde belirlendigi icin iki adlandirilmis istemci
/// vardir: normal ve self-signed sertifikalara izin veren. Adres ve API anahtari ise
/// fabrikadan alinan HttpClient ornegine yazilir (bu ornek paylasilmaz).
/// </summary>
public sealed class DokployClientFactory(
    IHttpClientFactory httpClientFactory,
    ILogger<DokployApiClient> logger) : IDokployClientFactory
{
    public const string ClientName = "dokploy";
    public const string InsecureClientName = "dokploy-insecure";

    public IDokployClient Create(DokployConnection connection)
    {
        var client = httpClientFactory.CreateClient(
            connection.AllowInvalidCertificates ? InsecureClientName : ClientName);

        client.BaseAddress = connection.ApiBaseUri();
        client.DefaultRequestHeaders.Add("x-api-key", connection.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Zaman asimini direnc katmani yonetiyor.
        client.Timeout = Timeout.InfiniteTimeSpan;

        return new DokployApiClient(client, connection, logger);
    }
}
