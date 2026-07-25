using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DokployMonitor.Core.Notifications;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Web.Options;
using DokployMonitor.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Web.Controllers;

/// <summary>
/// Dokploy'un generic webhook saglayicisinin hedefi.
///
/// Dokploy > Settings > Notifications > Webhook:
///   URL: https://monitor.example.com/api/webhooks/dokploy?token=GIZLI_ANAHTAR
///
/// Webhook, build biter bitmez gelir; polling'i beklemeden panoyu tazelemek icin
/// senkronizasyonu tetikler. Payload'da deploymentId olmadigindan bildirim ayri
/// bir tabloda saklanir, buildLink icindeki kimlikler ayikilir.
/// </summary>
[ApiController]
[Route("api/webhooks")]
// Dokploy bu ucu oturum acmadan cagirir; yetki denetimi URL'deki token ile yapilir.
[AllowAnonymous]
public sealed partial class WebhooksController(
    MonitorDbContext db,
    MonitorState state,
    DeploymentSyncService sync,
    IOptions<WebhookOptions> options,
    ILogger<WebhooksController> logger) : ControllerBase
{
    private readonly WebhookOptions _options = options.Value;

    [HttpPost("dokploy")]
    public async Task<IActionResult> Dokploy([FromQuery] string? token, [FromBody] JsonElement payload, CancellationToken ct)
    {
        // Token yapilandirilmamissa uc tamamen kapali: yanlislikla acik birakilmasin.
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            logger.LogWarning("Webhook cagrisi geldi ama Webhook:Token tanimli degil; istek reddedildi.");
            return NotFound();
        }

        if (!IsTokenValid(token))
        {
            logger.LogWarning("Gecersiz webhook token'i ile istek geldi.");
            return Unauthorized();
        }

        var notification = Map(payload);
        db.WebhookNotifications.Add(notification);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Webhook: {Title} / {Project} / {App} ({Status})",
            notification.Title,
            notification.ProjectName,
            notification.ApplicationName,
            notification.Status);

        // Deployment tablosunu hemen tazele; kullanici sonucu saniyeler icinde gorsun.
        state.RequestSync(SyncTrigger.Webhook);
        await sync.BroadcastAsync(ct);

        return Ok(new { received = true });
    }

    private bool IsTokenValid(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        // Zamanlama saldirilarina karsi sabit sureli karsilastirma.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(_options.Token!));
    }

    private static WebhookNotification Map(JsonElement payload)
    {
        var buildLink = Str(payload, "buildLink");

        return new WebhookNotification
        {
            ReceivedAt = DateTimeOffset.UtcNow,
            OccurredAt = ParseDate(Str(payload, "timestamp")),
            Title = Str(payload, "title"),
            Message = Str(payload, "message"),
            Status = Str(payload, "status"),
            Type = Str(payload, "type"),
            ProjectName = Str(payload, "projectName"),
            ApplicationName = Str(payload, "applicationName"),
            ApplicationType = Str(payload, "applicationType"),
            ErrorMessage = Str(payload, "errorMessage"),
            Domains = Str(payload, "domains"),
            BuildLink = buildLink,
            ServiceId = ExtractSegment(buildLink, ServiceIdPattern()),
            ProjectId = ExtractSegment(buildLink, ProjectIdPattern()),
            RawJson = payload.GetRawText(),
        };
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string? ExtractSegment(string? buildLink, Regex pattern)
    {
        if (string.IsNullOrWhiteSpace(buildLink))
        {
            return null;
        }

        var match = pattern.Match(buildLink);
        return match.Success ? match.Groups[1].Value : null;
    }

    // .../services/application/<id>?tab=deployments  ya da .../services/compose/<id>
    [GeneratedRegex(@"/services/(?:application|compose|application-preview)/([\w-]+)")]
    private static partial Regex ServiceIdPattern();

    [GeneratedRegex(@"/project/([\w-]+)")]
    private static partial Regex ProjectIdPattern();
}
