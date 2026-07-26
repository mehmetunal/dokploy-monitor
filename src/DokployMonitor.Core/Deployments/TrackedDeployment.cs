namespace DokployMonitor.Core.Deployments;

/// <summary>
/// Dokploy'daki bir deployment kaydinin bizdeki kalici kopyasi.
/// Dokploy eski deployment'lari ve loglarini zamanla temizledigi icin gecmisi
/// burada tutuyoruz; ekranlarin tamami bu tablodan besleniyor.
/// </summary>
public class TrackedDeployment
{
    /// <summary>Dokploy'daki `deploymentId` (birincil anahtar olarak da bunu kullaniyoruz).</summary>
    public required string DeploymentId { get; set; }

    /// <summary>
    /// Kaydin hangi Dokploy baglantisindan (API anahtarindan) geldigi.
    /// Coklu baglanti oncesi toplanan kayitlarda bos olabilir.
    /// </summary>
    public string? ConnectionId { get; set; }

    public DeploymentStatus Status { get; set; }

    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Dokploy sunucusundaki mutlak log dosyasi yolu (or. /etc/dokploy/logs/...).</summary>
    public string? LogPath { get; set; }

    /// <summary>Calisan build surecinin PID'i; kill islemi icin Dokploy tarafinda kullaniliyor.</summary>
    public string? Pid { get; set; }

    // --- Servis kimligi: bir deployment ya application'a ya compose'a ya da server'a ait ---
    public string? ApplicationId { get; set; }
    public string? ComposeId { get; set; }
    public string? ServerId { get; set; }
    public string? ScheduleId { get; set; }
    public string? BackupId { get; set; }
    public string? VolumeBackupId { get; set; }
    public string? PreviewDeploymentId { get; set; }
    public bool IsPreviewDeployment { get; set; }

    /// <summary>application | compose | server | schedule | backup | volumeBackup | previewDeployment</summary>
    public required string ServiceType { get; set; }

    /// <summary>Servisin (application/compose) kimligi; filtreleme ve gruplama icin tek alan.</summary>
    public string? ServiceId { get; set; }

    public string? ServiceName { get; set; }
    public string? AppName { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? EnvironmentId { get; set; }
    public string? EnvironmentName { get; set; }
    public string? ServerName { get; set; }
    public string? BuildServerName { get; set; }

    // --- Zaman bilgileri ---
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Bitmis deployment'lar icin saniye cinsinden sure; devam edenlerde null.</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>Hata mesajindan turetilen imza (gruplama icin). Bkz. ErrorSignature.</summary>
    public string? ErrorSignatureHash { get; set; }

    // --- Bizim izleme meta verimiz ---
    /// <summary>Bu kaydi ilk kez gordugumuz an.</summary>
    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>Kayitta en son degisiklik gordugumuz an.</summary>
    public DateTimeOffset LastUpdatedAt { get; set; }

    /// <summary>Hatali deployment'in arsivlenmis log dosyasinin bizdeki yolu (varsa).</summary>
    public string? ArchivedLogPath { get; set; }

    /// <summary>Dokploy'dan gelen ham JSON; ileride yeni alanlar eklenirse veri kaybetmemek icin.</summary>
    public string? RawJson { get; set; }

    /// <summary>Panelde gosterilecek okunabilir servis etiketi.</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(ServiceName) ? ServiceName!
        : !string.IsNullOrWhiteSpace(AppName) ? AppName!
        : ServiceId ?? DeploymentId;

    /// <summary>Devam eden deployment'lar icin anlik gecen sure, bitmisler icin toplam sure.</summary>
    public TimeSpan Elapsed(DateTimeOffset now)
    {
        var start = StartedAt ?? CreatedAt;
        var end = FinishedAt ?? now;
        var elapsed = end - start;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }
}
