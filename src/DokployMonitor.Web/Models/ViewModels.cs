using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.Dashboard;
using DokployMonitor.Core.Dokploy;
using DokployMonitor.Infrastructure.Caching;
using DokployMonitor.Core.Deployments;

namespace DokployMonitor.Web.Models;

public sealed class DeploymentHistoryViewModel
{
    public required IReadOnlyList<TrackedDeployment> Deployments { get; init; }
    public required IReadOnlyList<string> Projects { get; init; }
    public required DeploymentFilter Filter { get; init; }
    public required PageInfo Page { get; init; }

    /// <summary>Baglanti kimligi -> ad (filtre kutusu ve satir etiketleri icin).</summary>
    public required IReadOnlyDictionary<string, string> ConnectionNames { get; init; }
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

/// <summary>Bir Dokploy baglantisinin saglik sonucu; devre disi baglantilarda <c>Health</c> bostur.</summary>
public sealed class ConnectionHealth
{
    public required DokployConnection Connection { get; init; }
    public DokployHealth? Health { get; init; }
}

/// <summary>Kurulum tanilama ekrani: tum Dokploy baglantilari + container log erisimi.</summary>
public sealed class DiagnosticsViewModel
{
    public required IReadOnlyList<ConnectionHealth> Connections { get; init; }
    public required ContainerLogHealth Docker { get; init; }
    public required CacheHealth Cache { get; init; }
}

public sealed class ErrorAnalysisViewModel
{
    public required IReadOnlyList<ErrorGroupRow> Signatures { get; init; }
    public required IReadOnlyList<TrackedDeployment> RecentFailures { get; init; }
    public required IReadOnlyList<string> Projects { get; init; }
    public required ErrorFilter Filter { get; init; }
}
