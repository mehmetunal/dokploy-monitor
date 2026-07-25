namespace DokployMonitor.Infrastructure.Logs;

public sealed class LogOptions
{
    public const string SectionName = "Logs";

    /// <summary>
    /// Dokploy'un log klasorunun bu konteyner icindeki mount noktasi.
    /// Docker: -v /etc/dokploy/logs:/app/dokploy-logs:ro
    /// </summary>
    public string MountPath { get; set; } = "/app/dokploy-logs";

    /// <summary>
    /// Dokploy'un veritabanina yazdigi logPath degerlerinin kok dizini.
    /// Mount noktasina cevirmek icin bu on ek kirpilir.
    /// </summary>
    public string HostPath { get; set; } = "/etc/dokploy/logs";

    /// <summary>Hatali deployment loglarinin kalici kopyalandigi dizin.</summary>
    public string ArchivePath { get; set; } = "/app/data/log-archive";

    /// <summary>Detay ekraninda ilk yuklemede gosterilecek satir sayisi.</summary>
    public int DefaultTailLines { get; set; } = 400;

    /// <summary>Canli takipte iki okuma arasindaki bekleme (ms).</summary>
    public int PollIntervalMs { get; set; } = 750;

    /// <summary>Hatali deployment'larin logunu otomatik arsivle.</summary>
    public bool ArchiveFailedDeployments { get; set; } = true;
}
