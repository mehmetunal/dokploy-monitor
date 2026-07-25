namespace DokployMonitor.Core.Notifications;

/// <summary>
/// Dokploy'un generic webhook saglayicisindan gelen bildirim.
///
/// Dokploy bu payload'da deploymentId gondermez; sadece proje/uygulama adi ve
/// build linki vardir. Bu yuzden deployment tablosuna FK ile baglanmaz —
/// buildLink icindeki kimlikler ayikilarak servis eslestirmesi yapilir.
/// Webhook'un degeri hizdir: build biter bitmez (polling'i beklemeden) panoya duser.
/// </summary>
public class WebhookNotification
{
    public long Id { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Dokploy'un gonderdigi olay zamani (payload'daki timestamp).</summary>
    public DateTimeOffset? OccurredAt { get; set; }

    /// <summary>or. "Build Success", "Build Error", "Test Notification"</summary>
    public string? Title { get; set; }

    public string? Message { get; set; }

    /// <summary>success | error | (Dokploy'un gonderdigi diger degerler)</summary>
    public string? Status { get; set; }

    /// <summary>build | backup | ... </summary>
    public string? Type { get; set; }

    public string? ProjectName { get; set; }
    public string? ApplicationName { get; set; }
    public string? ApplicationType { get; set; }
    public string? ErrorMessage { get; set; }
    public string? BuildLink { get; set; }
    public string? Domains { get; set; }

    /// <summary>buildLink'ten ayiklanan uygulama/compose kimligi (varsa).</summary>
    public string? ServiceId { get; set; }

    /// <summary>buildLink'ten ayiklanan proje kimligi (varsa).</summary>
    public string? ProjectId { get; set; }

    public string? RawJson { get; set; }

    public bool IsError => string.Equals(Status, "error", StringComparison.OrdinalIgnoreCase);
}
