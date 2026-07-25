using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.Dashboard;
using DokployMonitor.Core.Deployments;

namespace DokployMonitor.Web.Models;

public sealed class DeploymentHistoryViewModel
{
    public required IReadOnlyList<TrackedDeployment> Deployments { get; init; }
    public required IReadOnlyList<string> Projects { get; init; }
    public required DeploymentFilter Filter { get; init; }
}

public sealed class DeploymentDetailsViewModel
{
    public required TrackedDeployment Deployment { get; init; }
    public required IReadOnlyList<DeploymentEvent> Events { get; init; }

    /// <summary>Ayni servisin son deploymentlari.</summary>
    public required IReadOnlyList<TrackedDeployment> History { get; init; }

    /// <summary>Ayni projedeki diger servislerin son deploymentlari.</summary>
    public required IReadOnlyList<TrackedDeployment> ProjectHistory { get; init; }

    public required LogReadResult Log { get; init; }

    /// <summary>Deployment devam ediyorsa ve log dosyasi okunabiliyorsa canli akis acilir.</summary>
    public bool CanStreamLive { get; init; }
}

/// <summary>Kurulum tanilama ekrani: Dokploy baglantisi + container log erisimi.</summary>
public sealed class DiagnosticsViewModel
{
    public required DokployHealth Dokploy { get; init; }
    public required ContainerLogHealth Docker { get; init; }
}

public sealed class ErrorAnalysisViewModel
{
    public required IReadOnlyList<ErrorGroupRow> Signatures { get; init; }
    public required IReadOnlyList<TrackedDeployment> RecentFailures { get; init; }
    public required IReadOnlyList<string> Projects { get; init; }
    public required ErrorFilter Filter { get; init; }
}
