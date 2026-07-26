using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.Deployments;
using DokployMonitor.Core.Dokploy;
using DokployMonitor.Core.Queueing;
using DokployMonitor.Infrastructure.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace DokployMonitor.Infrastructure.Dokploy;

/// <summary>
/// Dokploy REST API istemcisi.
///
/// Iki calisma modu var:
///  1. Merkezi mod (tercih edilen): `deployment.allCentralized` ile tum organizasyonun
///     deployment'lari tek istekte gelir — proje/ortam/servis bilgileri de gomulu.
///  2. Legacy mod: bu endpoint yoksa (eski Dokploy) `project.all` ile envanter cikarilir,
///     ardindan her servis icin `deployment.all` / `deployment.allByCompose` cagrilir.
/// Mod, ilk cagrida otomatik tespit edilir ve hatirlanir.
/// </summary>
/// <remarks>
/// Ornekler tek bir baglantiya baglidir ve <see cref="DokployClientFactory"/> tarafindan
/// uretilir; merkezi/legacy mod tespiti ornek icinde hatirlanir.
/// </remarks>
public sealed class DokployApiClient(
    HttpClient httpClient,
    DokployConnection connection,
    IStringLocalizer<SharedResource> text,
    ILogger<DokployApiClient> logger) : IDokployClient
{
    /// <summary>Bu istemcinin bagli oldugu Dokploy baglantisi.</summary>
    public DokployConnection Connection { get; } = connection;

    /// <summary>null = henuz denenmedi, true/false = tespit edildi.</summary>
    private bool? _supportsCentralized;
    private bool? _supportsQueueList;

    public async Task<IReadOnlyList<TrackedDeployment>> GetAllDeploymentsAsync(CancellationToken ct = default)
    {
        if (!connection.ForceLegacyDiscovery && _supportsCentralized != false)
        {
            var (ok, root) = await TryGetJsonAsync("deployment.allCentralized", ct);
            if (ok)
            {
                _supportsCentralized = true;
                return [.. root.AsArray().Select(MapDeployment).OfType<TrackedDeployment>()];
            }

            if (_supportsCentralized is null)
            {
                logger.LogWarning(
                    "deployment.allCentralized kullanilamiyor; servis servis toplama moduna geciliyor. " +
                    "Bu mod daha fazla istek uretir.");
            }

            _supportsCentralized = false;
        }

        return await GetAllDeploymentsLegacyAsync(ct);
    }

    public async Task<QueueSnapshot> GetQueueAsync(CancellationToken ct = default)
    {
        if (_supportsQueueList == false)
        {
            return Unavailable(text["This Dokploy version does not support the deployment.queueList endpoint."]);
        }

        var (ok, root) = await TryGetJsonAsync("deployment.queueList", ct);
        if (!ok)
        {
            if (_supportsQueueList is null)
            {
                logger.LogWarning("deployment.queueList okunamadi; kuyruk gorunumu devre disi kalacak.");
            }

            _supportsQueueList = false;
            return Unavailable(text["Queue information could not be read from Dokploy (endpoint missing or insufficient permission)."]);
        }

        _supportsQueueList = true;
        var jobs = root.AsArray().Select(MapQueueJob).OfType<QueueJob>().ToList();

        return new QueueSnapshot { Jobs = jobs, CapturedAt = DateTimeOffset.UtcNow };

        QueueSnapshot Unavailable(string reason) => new()
        {
            Jobs = [],
            CapturedAt = DateTimeOffset.UtcNow,
            UnavailableReason = reason,
        };
    }

    public Task KillDeploymentAsync(string deploymentId, CancellationToken ct = default) =>
        PostAsync("deployment.killProcess", new { deploymentId }, ct);

    public Task RedeployApplicationAsync(string applicationId, string? title = null, CancellationToken ct = default) =>
        PostAsync("application.redeploy", new
        {
            applicationId,
            title = title ?? "Redeploy (Monitor)",
            description = "DokployMonitor panelinden tetiklendi",
        }, ct);

    public Task RedeployComposeAsync(string composeId, string? title = null, CancellationToken ct = default) =>
        PostAsync("compose.redeploy", new
        {
            composeId,
            title = title ?? "Redeploy (Monitor)",
            description = "DokployMonitor panelinden tetiklendi",
        }, ct);

    public async Task<DokployHealth> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("project.all", ct);
            var authorized = response.StatusCode != HttpStatusCode.Unauthorized
                && response.StatusCode != HttpStatusCode.Forbidden;

            if (!authorized)
            {
                return new DokployHealth(true, false, false, false,
                    text["The API key was rejected ({0}).", (int)response.StatusCode]);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new DokployHealth(true, true, false, false,
                    text["project.all returned an unexpected response ({0}).", (int)response.StatusCode]);
            }

            var (centralized, _) = await TryGetJsonAsync("deployment.allCentralized", ct);
            var (queue, _) = await TryGetJsonAsync("deployment.queueList", ct);
            _supportsCentralized = centralized;
            _supportsQueueList = queue;

            return new DokployHealth(true, true, centralized, queue, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new DokployHealth(false, false, false, false, ex.Message);
        }
    }

    // ---------------------------------------------------------------- HTTP

    private async Task<(bool Ok, JsonElement Root)> TryGetJsonAsync(string path, CancellationToken ct)
    {
        try
        {
            using var response = await httpClient.GetAsync(path, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Dokploy {Path} -> HTTP {Status}", path, (int)response.StatusCode);
                return (false, default);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            // JsonDocument dispose edilince JsonElement gecersiz kalir; klonluyoruz.
            return (true, document.RootElement.Clone());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Dokploy {Path} cagrisi basarisiz.", path);
            return (false, default);
        }
    }

    private async Task PostAsync(string path, object body, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync(path, body, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new DokployApiException(
                text["The Dokploy {0} call failed ({1}): {2}", path, (int)response.StatusCode, Truncate(detail, 500)]);
        }
    }

    // ------------------------------------------------------------- Legacy

    private async Task<IReadOnlyList<TrackedDeployment>> GetAllDeploymentsLegacyAsync(CancellationToken ct)
    {
        var (ok, projectsRoot) = await TryGetJsonAsync("project.all", ct);
        if (!ok)
        {
            throw new DokployApiException(
                text["Could not read project.all; Dokploy is unreachable or the API key is invalid."]);
        }

        var services = EnumerateServices(projectsRoot).ToList();
        var gate = new SemaphoreSlim(Math.Max(1, connection.MaxParallelRequests));
        var results = new List<TrackedDeployment>[services.Count];

        await Parallel.ForAsync(0, services.Count, ct, async (index, token) =>
        {
            await gate.WaitAsync(token);
            try
            {
                var service = services[index];
                var path = service.Type == "compose"
                    ? $"deployment.allByCompose?composeId={Uri.EscapeDataString(service.Id)}"
                    : $"deployment.all?applicationId={Uri.EscapeDataString(service.Id)}";

                var (fetched, root) = await TryGetJsonAsync(path, token);
                if (!fetched)
                {
                    results[index] = [];
                    return;
                }

                results[index] =
                [
                    .. root.AsArray()
                        .Select(MapDeployment)
                        .OfType<TrackedDeployment>()
                        .Select(d => Enrich(d, service)),
                ];
            }
            finally
            {
                gate.Release();
            }
        });

        return [.. results.Where(r => r is not null).SelectMany(r => r)];
    }

    private sealed record ServiceRef(string Id, string Type, string Name, string? ProjectId, string? ProjectName, string? EnvironmentId, string? EnvironmentName);

    /// <summary>
    /// project.all yanitindan servis envanterini cikarir.
    /// Yeni surumler projeleri environment katmani altinda tutuyor, eskiler dogrudan;
    /// iki sekli de destekliyoruz.
    /// </summary>
    private static IEnumerable<ServiceRef> EnumerateServices(JsonElement projectsRoot)
    {
        foreach (var project in projectsRoot.AsArray())
        {
            var projectId = project.Str("projectId");
            var projectName = project.Str("name");

            var environments = project.Prop("environments").AsArray();
            if (environments.Count > 0)
            {
                foreach (var environment in environments)
                {
                    foreach (var service in ServicesIn(environment, projectId, projectName, environment.Str("environmentId"), environment.Str("name")))
                    {
                        yield return service;
                    }
                }
            }
            else
            {
                foreach (var service in ServicesIn(project, projectId, projectName, null, null))
                {
                    yield return service;
                }
            }
        }

        static IEnumerable<ServiceRef> ServicesIn(JsonElement container, string? projectId, string? projectName, string? envId, string? envName)
        {
            foreach (var app in container.Prop("applications").AsArray())
            {
                var id = app.Str("applicationId");
                if (id is not null)
                {
                    yield return new ServiceRef(id, "application", app.Str("name") ?? app.Str("appName") ?? id, projectId, projectName, envId, envName);
                }
            }

            foreach (var compose in container.Prop("compose").AsArray())
            {
                var id = compose.Str("composeId");
                if (id is not null)
                {
                    yield return new ServiceRef(id, "compose", compose.Str("name") ?? compose.Str("appName") ?? id, projectId, projectName, envId, envName);
                }
            }
        }
    }

    private static TrackedDeployment Enrich(TrackedDeployment deployment, ServiceRef service)
    {
        deployment.ServiceId ??= service.Id;
        deployment.ServiceName ??= service.Name;
        deployment.ProjectId ??= service.ProjectId;
        deployment.ProjectName ??= service.ProjectName;
        deployment.EnvironmentId ??= service.EnvironmentId;
        deployment.EnvironmentName ??= service.EnvironmentName;
        return deployment;
    }

    // ------------------------------------------------------------ Mapping

    private static TrackedDeployment? MapDeployment(JsonElement element)
    {
        var deploymentId = element.Str("deploymentId");
        if (string.IsNullOrWhiteSpace(deploymentId))
        {
            return null;
        }

        var applicationId = element.Str("applicationId");
        var composeId = element.Str("composeId");
        var isPreview = element.Bool("isPreviewDeployment");

        var createdAt = element.Date("createdAt") ?? DateTimeOffset.UtcNow;
        var startedAt = element.Date("startedAt");
        var finishedAt = element.Date("finishedAt");

        var deployment = new TrackedDeployment
        {
            DeploymentId = deploymentId,
            Status = DeploymentStatusExtensions.ParseDokployStatus(element.Str("status")),
            Title = element.Str("title"),
            Description = element.Str("description"),
            ErrorMessage = element.Str("errorMessage"),
            LogPath = element.Str("logPath"),
            Pid = element.Str("pid"),
            ApplicationId = applicationId,
            ComposeId = composeId,
            ServerId = element.Str("serverId"),
            ScheduleId = element.Str("scheduleId"),
            BackupId = element.Str("backupId"),
            VolumeBackupId = element.Str("volumeBackupId"),
            PreviewDeploymentId = element.Str("previewDeploymentId"),
            IsPreviewDeployment = isPreview,
            ServiceType = ResolveServiceType(element, applicationId, composeId, isPreview),
            ServiceId = applicationId ?? composeId,
            CreatedAt = createdAt,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            DurationSeconds = finishedAt is null
                ? null
                : (int)Math.Max(0, (finishedAt.Value - (startedAt ?? createdAt)).TotalSeconds),
            RawJson = element.GetRawText(),
        };

        // Merkezi endpoint servis/proje bilgisini gomulu doner; legacy modda bunlar bos kalir.
        var serviceNode = element.Prop("application") ?? element.Prop("compose");
        if (serviceNode is { } service)
        {
            deployment.ServiceName = service.Str("name") ?? service.Str("appName");
            deployment.AppName = service.Str("appName");

            var environment = service.Prop("environment");
            deployment.EnvironmentId = environment.Str("environmentId");
            deployment.EnvironmentName = environment.Str("name");

            var project = environment.Prop("project");
            deployment.ProjectId = project.Str("projectId");
            deployment.ProjectName = project.Str("name");

            deployment.ServerName = service.Prop("server").Str("name");
            deployment.BuildServerName = service.Prop("buildServer").Str("name");
        }

        deployment.ServerName ??= element.Prop("server").Str("name");
        deployment.BuildServerName ??= element.Prop("buildServer").Str("name");

        return deployment;
    }

    private static string ResolveServiceType(JsonElement element, string? applicationId, string? composeId, bool isPreview)
    {
        if (isPreview || element.Str("previewDeploymentId") is not null)
        {
            return "previewDeployment";
        }

        if (composeId is not null)
        {
            return "compose";
        }

        if (applicationId is not null)
        {
            return "application";
        }

        if (element.Str("scheduleId") is not null)
        {
            return "schedule";
        }

        if (element.Str("backupId") is not null)
        {
            return "backup";
        }

        if (element.Str("volumeBackupId") is not null)
        {
            return "volumeBackup";
        }

        return element.Str("serverId") is not null ? "server" : "unknown";
    }

    private static QueueJob? MapQueueJob(JsonElement element)
    {
        var id = element.Str("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var data = element.Prop("data");

        return new QueueJob
        {
            Id = id,
            State = element.Str("state") ?? "unknown",
            ApplicationType = data.Str("applicationType"),
            JobType = data.Str("type"),
            ApplicationId = data.Str("applicationId"),
            ComposeId = data.Str("composeId"),
            PreviewDeploymentId = data.Str("previewDeploymentId"),
            Title = data.Str("titleLog"),
            Description = data.Str("descriptionLog"),
            ServerId = data.Str("serverId"),
            ServicePath = element.Str("servicePath"),
            EnqueuedAt = element.Date("timestamp"),
            ProcessedAt = element.Date("processedOn"),
            FinishedAt = element.Date("finishedOn"),
            FailedReason = element.Str("failedReason"),
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : string.Concat(value.AsSpan(0, max), "...");
}

public sealed class DokployApiException(string message) : Exception(message);
