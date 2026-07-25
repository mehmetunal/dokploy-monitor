namespace DokployMonitor.Core.Deployments;

/// <summary>
/// Dokploy'un deployment durumlari. Kaynak: Dokploy `deploymentStatus` enum'u
/// ("running" | "done" | "error" | "cancelled"). Taninmayan bir deger gelirse
/// <see cref="Unknown"/> kullanilir; boylece Dokploy yeni bir durum eklerse
/// senkronizasyon patlamaz.
/// </summary>
public enum DeploymentStatus
{
    Unknown = 0,
    Running = 1,
    Done = 2,
    Error = 3,
    Cancelled = 4,
}

public static class DeploymentStatusExtensions
{
    public static DeploymentStatus ParseDokployStatus(string? value) => value?.ToLowerInvariant() switch
    {
        "running" => DeploymentStatus.Running,
        "done" => DeploymentStatus.Done,
        "error" => DeploymentStatus.Error,
        "cancelled" or "canceled" => DeploymentStatus.Cancelled,
        _ => DeploymentStatus.Unknown,
    };

    /// <summary>Deployment hala devam ediyor mu?</summary>
    public static bool IsActive(this DeploymentStatus status) => status is DeploymentStatus.Running;

    /// <summary>Basarisiz bitmis mi? (Kill edilen deployment'lar Dokploy tarafinda "error" yazilir.)</summary>
    public static bool IsFailure(this DeploymentStatus status) =>
        status is DeploymentStatus.Error or DeploymentStatus.Cancelled;
}
