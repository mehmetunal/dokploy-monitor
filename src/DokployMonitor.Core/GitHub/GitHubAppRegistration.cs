namespace DokployMonitor.Core.GitHub;

/// <summary>
/// GitHub App Manifest akisiyla olusturulmus uygulama kaydi.
/// Kullanici API anahtari girmez; Client Secret / Private Key GitHub'dan otomatik gelir.
/// </summary>
public class GitHubAppRegistration
{
    public required string Id { get; set; }

    /// <summary>GitHub App sayisal kimligi (JWT icin).</summary>
    public long AppId { get; set; }

    public required string ClientId { get; set; }

    public required string ClientSecret { get; set; }

    /// <summary>PEM formatinda private key (installation token uretimi).</summary>
    public required string PrivateKeyPem { get; set; }

    public string? WebhookSecret { get; set; }

    /// <summary>Kurulum URL'si icin: https://github.com/apps/{Slug}</summary>
    public required string Slug { get; set; }

    public required string Name { get; set; }

    public string? HtmlUrl { get; set; }

    public string? OwnerLogin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
