namespace DokployMonitor.Core.Abstractions;

/// <summary>
/// Calisan servisin container loglarini (`docker logs` karsiligi) okur.
///
/// Build logu ile ayni sey degildir: build logu Dokploy'un derleme cikisi (dosyadan
/// okunur), container logu ise uygulamanin kendi stdout/stderr cikisidir. Kaynak
/// Docker Engine API'sidir; konteynere `/var/run/docker.sock` mount edilmesi gerekir.
/// </summary>
public interface IContainerLogReader
{
    /// <summary>
    /// Swarm servisi ya da container adinin son <paramref name="maxLines"/> satirini doner.
    /// Dokploy uygulamalari swarm servisi olarak calistigi icin once servis, bulunamazsa
    /// ayni adli container denenir.
    /// </summary>
    Task<LogReadResult> ReadTailAsync(string? serviceOrContainerName, int maxLines, CancellationToken ct = default);

    /// <summary>Docker soketine erisilebiliyor mu (Tanilama ekrani icin)?</summary>
    Task<ContainerLogHealth> CheckHealthAsync(CancellationToken ct = default);
}

/// <param name="Enabled">Yapilandirmada acik mi?</param>
/// <param name="SocketExists">Soket dosyasi konteyner icinde gorunuyor mu?</param>
/// <param name="Reachable">Engine API yanit veriyor mu?</param>
public sealed record ContainerLogHealth(
    bool Enabled,
    bool SocketExists,
    bool Reachable,
    string? ServerVersion,
    string? Message);
