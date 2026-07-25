namespace DokployMonitor.Core.Deployments;

public enum DeploymentEventType
{
    /// <summary>Deployment kaydini ilk kez "running" durumunda gorduk.</summary>
    Started = 1,

    /// <summary>Durum degisti (or. running -> error).</summary>
    StatusChanged = 2,

    /// <summary>Deployment sonuclandi (done/error/cancelled).</summary>
    Finished = 3,

    /// <summary>Dokploy webhook'u bir build olayi bildirdi.</summary>
    WebhookReceived = 4,
}

public enum DeploymentEventSource
{
    /// <summary>Periyodik REST senkronizasyonu.</summary>
    Poll = 1,

    /// <summary>Dokploy generic webhook'u.</summary>
    Webhook = 2,

    /// <summary>Kuyruk (deployment.queueList) senkronizasyonu.</summary>
    Queue = 3,
}

/// <summary>
/// Deployment yasam dongusundeki her degisiklik icin bir denetim kaydi.
/// "Ne zaman basladi, ne zaman hata verdi, bunu nereden ogrendik" sorularinin cevabi.
/// </summary>
public class DeploymentEvent
{
    public long Id { get; set; }
    public required string DeploymentId { get; set; }
    public DeploymentEventType EventType { get; set; }
    public DeploymentEventSource Source { get; set; }

    public DeploymentStatus? FromStatus { get; set; }
    public DeploymentStatus? ToStatus { get; set; }

    public string? Message { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public TrackedDeployment? Deployment { get; set; }
}
