using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.Deployments;

namespace DokployMonitor.Web.Models;

public sealed class DeploymentHistoryViewModel
{
    public required IReadOnlyList<TrackedDeployment> Deployments { get; init; }
    public required IReadOnlyList<string> Projects { get; init; }
    public string? SelectedProject { get; init; }
    public string? SelectedStatus { get; init; }
    public string? Query { get; init; }
}

public sealed class DeploymentDetailsViewModel
{
    public required TrackedDeployment Deployment { get; init; }
    public required IReadOnlyList<DeploymentEvent> Events { get; init; }
    public required IReadOnlyList<TrackedDeployment> History { get; init; }
    public required LogReadResult Log { get; init; }

    /// <summary>Deployment devam ediyorsa ve log dosyasi okunabiliyorsa canli akis acilir.</summary>
    public bool CanStreamLive { get; init; }
}

public sealed class ErrorAnalysisViewModel
{
    public required IReadOnlyList<ErrorSignature> Signatures { get; init; }
    public required IReadOnlyList<TrackedDeployment> RecentFailures { get; init; }
}
