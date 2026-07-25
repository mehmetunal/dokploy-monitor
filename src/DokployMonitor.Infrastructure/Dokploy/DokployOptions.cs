namespace DokployMonitor.Infrastructure.Dokploy;

public sealed class DokployOptions
{
    public const string SectionName = "Dokploy";

    /// <summary>
    /// Dokploy panelinin koku, /api olmadan. Ornek: https://dokploy.sirket.com
    /// Ayni sunucuda calisiyorsak http://dokploy:3000 (internal network) tercih edilir.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Dokploy > Settings > API Keys altindan uretilen anahtar (x-api-key).</summary>
    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// true ise `deployment.allCentralized` hic denenmez ve dogrudan proje/servis
    /// gezinerek toplanir. Eski Dokploy surumleri icin kacis kapisi.
    /// </summary>
    public bool ForceLegacyDiscovery { get; set; }

    /// <summary>Legacy modda es zamanli istek siniri (Dokploy'u yormamak icin).</summary>
    public int MaxParallelRequests { get; set; } = 4;

    /// <summary>Self-signed sertifika kullanan kurulumlar icin.</summary>
    public bool AllowInvalidCertificates { get; set; }

    public Uri ApiBaseUri()
    {
        var root = BaseUrl.TrimEnd('/');
        return new Uri($"{root}/api/", UriKind.Absolute);
    }
}
