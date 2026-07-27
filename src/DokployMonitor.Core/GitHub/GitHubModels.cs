namespace DokployMonitor.Core.GitHub;

public sealed record GitHubRepoInfo(
    long Id,
    string FullName,
    string Name,
    string OwnerLogin,
    bool Private,
    string DefaultBranch,
    string HtmlUrl,
    string? Description);

public sealed record GitHubBranchInfo(
    string Name,
    string Sha,
    bool Protected);

public sealed record GitHubPullRequestInfo(
    int Number,
    string Title,
    string State,
    string HeadRef,
    string BaseRef,
    string HtmlUrl,
    string UserLogin,
    bool Draft,
    bool Mergeable,
    bool Merged,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? MergedAt,
    DateTimeOffset? ClosedAt);

public sealed record GitHubCommitInfo(
    string Sha,
    string Message,
    string AuthorName,
    string? AuthorLogin,
    DateTimeOffset? CommittedAt,
    string HtmlUrl);

public sealed record GitHubManifestConversionResult(
    long AppId,
    string ClientId,
    string ClientSecret,
    string PrivateKeyPem,
    string? WebhookSecret,
    string Slug,
    string Name,
    string? HtmlUrl,
    string? OwnerLogin);

public sealed record GitHubInstallationInfo(
    long InstallationId,
    string AccountLogin,
    string AccountType,
    string? AccountAvatarUrl);
