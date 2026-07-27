using DokployMonitor.Core.GitHub;

namespace DokployMonitor.Core.Abstractions;

/// <summary>GitHub App kimlik dogrulama + repo/branch/PR islemleri.</summary>
public interface IGitHubAppClient
{
    Task<GitHubManifestConversionResult> ConvertManifestAsync(string code, CancellationToken ct = default);

    Task<IReadOnlyList<GitHubInstallationInfo>> ListAppInstallationsAsync(
        GitHubAppRegistration app,
        CancellationToken ct = default);

    Task<IReadOnlyList<GitHubRepoInfo>> ListInstallationRepositoriesAsync(
        GitHubAppRegistration app,
        long installationId,
        CancellationToken ct = default);

    Task<IReadOnlyList<GitHubBranchInfo>> ListBranchesAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        CancellationToken ct = default);

    Task<IReadOnlyList<GitHubPullRequestInfo>> ListPullRequestsAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        string state = "open",
        int maxPages = 2,
        CancellationToken ct = default);

    Task<IReadOnlyList<GitHubCommitInfo>> ListCommitsAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        string? sha = null,
        int perPage = 30,
        CancellationToken ct = default);

    Task CreateBranchAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        string newBranch,
        string fromBranch,
        CancellationToken ct = default);

    Task DeleteBranchAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        string branch,
        CancellationToken ct = default);

    Task MergeBranchesAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        string baseBranch,
        string headBranch,
        string? commitMessage,
        CancellationToken ct = default);

    Task MergePullRequestAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        int pullNumber,
        string? commitTitle,
        CancellationToken ct = default);

    Task ReviewPullRequestAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        int pullNumber,
        string eventType,
        string? body,
        CancellationToken ct = default);

    Task ClosePullRequestAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        int pullNumber,
        CancellationToken ct = default);
}
