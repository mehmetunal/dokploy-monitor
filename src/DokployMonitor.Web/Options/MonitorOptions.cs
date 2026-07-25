namespace DokployMonitor.Web.Options;

public sealed class MonitorOptions
{
    public const string SectionName = "Monitor";

    /// <summary>Hicbir deployment calismiyorken iki senkronizasyon arasindaki sure (sn).</summary>
    public int IdlePollSeconds { get; set; } = 15;

    /// <summary>En az bir deployment calisirken senkronizasyon araligi (sn).</summary>
    public int ActivePollSeconds { get; set; } = 2;

    /// <summary>Kuyruk (queueList) sorgulama araligi (sn).</summary>
    public int QueuePollSeconds { get; set; } = 5;

    /// <summary>Ana panoda gosterilecek "son deploymentlar" satir sayisi.</summary>
    public int RecentCount { get; set; } = 50;

    /// <summary>Bu gunden eski deployment kayitlari temizlenir. 0 = hic silme.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Ilk senkronizasyonda gecmis kayitlar icin bildirim uretilmez. Bundan daha
    /// yeni biten deployment'lar "yeni sonuclandi" sayilir (dakika).
    /// </summary>
    public int FreshFinishWindowMinutes { get; set; } = 10;
}

public sealed class WebhookOptions
{
    public const string SectionName = "Webhook";

    /// <summary>
    /// Dokploy webhook URL'ine eklenecek gizli anahtar:
    /// https://monitor.example.com/api/webhooks/dokploy?token=BU_DEGER
    /// Bos birakilirsa webhook ucu 404 doner (kapali).
    /// </summary>
    public string? Token { get; set; }
}
