namespace DokployMonitor.Core.GitHub;

/// <summary>
/// Kullanicinin "Install App" ile bagladigi hesap/organizasyon kurulumu.
/// </summary>
public class GitHubInstallation
{
    public required string Id { get; set; }

    /// <summary>Hangi GitHub App kaydina ait.</summary>
    public required string AppRegistrationId { get; set; }

    /// <summary>GitHub installation_id.</summary>
    public long InstallationId { get; set; }

    public required string AccountLogin { get; set; }

    /// <summary>User veya Organization.</summary>
    public required string AccountType { get; set; }

    public string? AccountAvatarUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSyncedAt { get; set; }
}
