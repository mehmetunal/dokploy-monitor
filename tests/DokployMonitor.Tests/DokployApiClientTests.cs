using System.Net;
using System.Text;
using DokployMonitor.Core.Deployments;
using DokployMonitor.Core.Dokploy;
using DokployMonitor.Infrastructure.Dokploy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Tests;

/// <summary>
/// Dokploy'un OpenAPI dokumani yanit govdelerini bos ("{}") tanimliyor, yani
/// alanlari sozlesmeden dogrulayamiyoruz. Bu testler, gercek Dokploy yanitlarinin
/// (kaynak kodundan dogrulanmis sekliyle) dogru eslendigini garanti eder.
/// </summary>
public class DokployApiClientTests
{
    private const string CentralizedResponse = """
    [
      {
        "deploymentId": "dep-1",
        "title": "Manual deployment",
        "description": null,
        "status": "running",
        "logPath": "/etc/dokploy/logs/api/dep-1.log",
        "pid": "4711",
        "applicationId": "app-1",
        "composeId": null,
        "serverId": null,
        "isPreviewDeployment": false,
        "createdAt": "2026-07-25T10:00:00.000Z",
        "startedAt": "2026-07-25T10:00:05.000Z",
        "finishedAt": null,
        "errorMessage": null,
        "application": {
          "applicationId": "app-1",
          "name": "trimango-api",
          "appName": "trimango-api-abc",
          "environment": {
            "environmentId": "env-1",
            "name": "production",
            "project": { "projectId": "proj-1", "name": "Trimango" }
          },
          "server": { "serverId": "srv-1", "name": "vps-01", "serverType": "remote" },
          "buildServer": null
        },
        "compose": null
      },
      {
        "deploymentId": "dep-2",
        "title": "Auto deploy",
        "status": "error",
        "logPath": "/etc/dokploy/logs/web/dep-2.log",
        "composeId": "comp-1",
        "isPreviewDeployment": false,
        "createdAt": "2026-07-25T09:00:00.000Z",
        "startedAt": "2026-07-25T09:00:10.000Z",
        "finishedAt": "2026-07-25T09:02:10.000Z",
        "errorMessage": "npm ERR! code ELIFECYCLE",
        "compose": {
          "composeId": "comp-1",
          "name": "storefront",
          "appName": "storefront-xyz",
          "environment": {
            "environmentId": "env-2",
            "name": "staging",
            "project": { "projectId": "proj-2", "name": "Storefront" }
          },
          "server": null
        }
      }
    ]
    """;

    private const string QueueResponse = """
    [
      {
        "id": "3",
        "name": "deploy",
        "state": "waiting",
        "timestamp": 1785060000000,
        "servicePath": "Trimango / production / api",
        "data": {
          "applicationId": "app-9",
          "applicationType": "application",
          "type": "redeploy",
          "titleLog": "Redeploy",
          "descriptionLog": "manual"
        }
      },
      {
        "id": "2",
        "state": "active",
        "timestamp": 1785059000000,
        "processedOn": 1785059100000,
        "data": {
          "composeId": "comp-7",
          "applicationType": "compose",
          "type": "deploy",
          "titleLog": "Deploy",
          "serverId": "srv-2"
        }
      }
    ]
    """;

    [Fact]
    public async Task GetAllDeploymentsAsync_maps_centralized_response()
    {
        var client = CreateClient(("deployment.allCentralized", HttpStatusCode.OK, CentralizedResponse));

        var deployments = await client.GetAllDeploymentsAsync();

        Assert.Equal(2, deployments.Count);

        var running = deployments.Single(d => d.DeploymentId == "dep-1");
        Assert.Equal(DeploymentStatus.Running, running.Status);
        Assert.Equal("application", running.ServiceType);
        Assert.Equal("app-1", running.ServiceId);
        Assert.Equal("trimango-api", running.ServiceName);
        Assert.Equal("Trimango", running.ProjectName);
        Assert.Equal("production", running.EnvironmentName);
        Assert.Equal("vps-01", running.ServerName);
        Assert.Equal("/etc/dokploy/logs/api/dep-1.log", running.LogPath);
        Assert.Null(running.DurationSeconds);

        var failed = deployments.Single(d => d.DeploymentId == "dep-2");
        Assert.Equal(DeploymentStatus.Error, failed.Status);
        Assert.Equal("compose", failed.ServiceType);
        Assert.Equal("comp-1", failed.ServiceId);
        Assert.Equal("Storefront", failed.ProjectName);
        Assert.Equal("npm ERR! code ELIFECYCLE", failed.ErrorMessage);
        Assert.Equal(120, failed.DurationSeconds);
    }

    [Fact]
    public async Task GetAllDeploymentsAsync_falls_back_to_per_service_discovery()
    {
        const string projects = """
        [
          {
            "projectId": "proj-1",
            "name": "Trimango",
            "environments": [
              {
                "environmentId": "env-1",
                "name": "production",
                "applications": [{ "applicationId": "app-1", "name": "trimango-api" }],
                "compose": []
              }
            ]
          }
        ]
        """;

        const string appDeployments = """
        [{ "deploymentId": "dep-legacy", "status": "done", "logPath": "/etc/dokploy/logs/a.log",
           "applicationId": "app-1", "createdAt": "2026-07-25T08:00:00.000Z",
           "startedAt": "2026-07-25T08:00:00.000Z", "finishedAt": "2026-07-25T08:01:00.000Z" }]
        """;

        // Eski surumu taklit et: allCentralized 404 doner.
        var client = CreateClient(
            ("deployment.allCentralized", HttpStatusCode.NotFound, "{}"),
            ("project.all", HttpStatusCode.OK, projects),
            ("deployment.all", HttpStatusCode.OK, appDeployments));

        var deployments = await client.GetAllDeploymentsAsync();

        var single = Assert.Single(deployments);
        Assert.Equal("dep-legacy", single.DeploymentId);
        Assert.Equal(DeploymentStatus.Done, single.Status);
        Assert.Equal(60, single.DurationSeconds);

        // Legacy modda proje/servis adlari envanterden zenginlestirilir.
        Assert.Equal("trimango-api", single.ServiceName);
        Assert.Equal("Trimango", single.ProjectName);
        Assert.Equal("production", single.EnvironmentName);
    }

    [Fact]
    public async Task GetQueueAsync_maps_jobs_and_epoch_timestamps()
    {
        var client = CreateClient(("deployment.queueList", HttpStatusCode.OK, QueueResponse));

        var snapshot = await client.GetQueueAsync();

        Assert.True(snapshot.IsAvailable);
        Assert.Equal(2, snapshot.Jobs.Count);

        var waiting = Assert.Single(snapshot.Waiting);
        Assert.Equal("3", waiting.Id);
        Assert.Equal("app-9", waiting.ServiceId);
        Assert.Equal("redeploy", waiting.JobType);
        Assert.Equal("Trimango / production / api", waiting.ServicePath);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1785060000000), waiting.EnqueuedAt);
        Assert.Equal("__local__", waiting.Partition);

        var active = Assert.Single(snapshot.Active);
        Assert.Equal("comp-7", active.ServiceId);
        Assert.Equal("srv-2", active.Partition);
    }

    [Fact]
    public async Task GetQueueAsync_reports_unavailable_when_endpoint_missing()
    {
        var client = CreateClient(("deployment.queueList", HttpStatusCode.NotFound, "{}"));

        var snapshot = await client.GetQueueAsync();

        Assert.False(snapshot.IsAvailable);
        Assert.Empty(snapshot.Jobs);
        Assert.NotNull(snapshot.UnavailableReason);
    }

    [Fact]
    public async Task Requests_carry_the_api_key_header()
    {
        var handler = new StubHandler([("deployment.allCentralized", HttpStatusCode.OK, "[]")]);
        var client = CreateClient(handler);

        await client.GetAllDeploymentsAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("secret-key", request.Headers.GetValues("x-api-key").Single());
        Assert.StartsWith("https://dokploy.test/api/", request.RequestUri!.ToString());
    }

    private static DokployApiClient CreateClient(params (string Path, HttpStatusCode Status, string Body)[] routes) =>
        CreateClient(new StubHandler(routes));

    private static DokployApiClient CreateClient(StubHandler handler)
    {
        // Istemci artik tek bir baglantiya baglidir (bkz. DokployClientFactory).
        var connection = new DokployConnection
        {
            Id = "test",
            Name = "Test",
            BaseUrl = "https://dokploy.test",
            ApiKey = "secret-key",
        };

        var httpClient = new HttpClient(handler) { BaseAddress = connection.ApiBaseUri() };
        httpClient.DefaultRequestHeaders.Add("x-api-key", connection.ApiKey);

        return new DokployApiClient(httpClient, connection, NullLogger<DokployApiClient>.Instance);
    }

    private sealed class StubHandler(IReadOnlyList<(string Path, HttpStatusCode Status, string Body)> routes)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            var path = request.RequestUri!.AbsolutePath.Split('/').Last();
            var route = routes.FirstOrDefault(r => r.Path == path);

            var response = route.Path is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") }
                : new HttpResponseMessage(route.Status)
                {
                    Content = new StringContent(route.Body, Encoding.UTF8, "application/json"),
                };

            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
