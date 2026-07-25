using DokployMonitor.Core.Deployments;
using DokployMonitor.Core.Queueing;

namespace DokployMonitor.Core.Abstractions;

/// <summary>
/// Dokploy REST API'sine (x-api-key ile) erisim. Tum cagrilar sunucu tarafindadir;
/// API anahtari hicbir zaman tarayiciya gonderilmez.
/// </summary>
public interface IDokployClient
{
    /// <summary>
    /// Organizasyondaki tum deployment'lari doner.
    /// Tercihen tek cagrida (`deployment.allCentralized`); bu endpoint'i desteklemeyen
    /// eski surumlerde otomatik olarak proje/servis gezinerek toplar.
    /// </summary>
    Task<IReadOnlyList<TrackedDeployment>> GetAllDeploymentsAsync(CancellationToken ct = default);

    /// <summary>Kuyrugun anlik durumu (`deployment.queueList`).</summary>
    Task<QueueSnapshot> GetQueueAsync(CancellationToken ct = default);

    /// <summary>Calisan bir deployment'i sonlandirir (`deployment.killProcess`).</summary>
    Task KillDeploymentAsync(string deploymentId, CancellationToken ct = default);

    /// <summary>Uygulamayi yeniden deploy eder (`application.redeploy`).</summary>
    Task RedeployApplicationAsync(string applicationId, string? title = null, CancellationToken ct = default);

    /// <summary>Compose servisini yeniden deploy eder (`compose.redeploy`).</summary>
    Task RedeployComposeAsync(string composeId, string? title = null, CancellationToken ct = default);

    /// <summary>Baglantiyi ve API anahtarini dogrular; tanilama ekraninda kullanilir.</summary>
    Task<DokployHealth> CheckHealthAsync(CancellationToken ct = default);
}

/// <summary>Dokploy baglantisinin saglik/yetenek durumu.</summary>
public sealed record DokployHealth(
    bool IsReachable,
    bool IsAuthorized,
    bool SupportsCentralizedDeployments,
    bool SupportsQueueList,
    string? Error);
