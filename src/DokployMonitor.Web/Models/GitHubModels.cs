using DokployMonitor.Core.GitHub;

namespace DokployMonitor.Web.Models;

public sealed class GitHubIndexViewModel
{
    public GitHubAppRegistration? App { get; init; }

    public IReadOnlyList<GitHubInstallationViewModel> Installations { get; init; } = [];

    public string? SelectedInstallationId { get; init; }

    public GitHubInstallationViewModel? SelectedInstallation { get; init; }

    public IReadOnlyList<GitHubRepoInfo> Repositories { get; init; } = [];

    public string? RepoQuery { get; init; }

    public string PublicBaseUrl { get; init; } = "";

    public string ManifestJson { get; init; } = "";

    public string ManifestState { get; init; } = "";

    public string? Organization { get; init; }

    public bool CanManage { get; init; }
}

public sealed class GitHubInstallationViewModel
{
    public required string Id { get; init; }
    public long InstallationId { get; init; }
    public required string AccountLogin { get; init; }
    public required string AccountType { get; init; }
    public string? AccountAvatarUrl { get; init; }
}

public sealed class GitHubRepoViewModel
{
    public required GitHubAppRegistration App { get; init; }
    public required GitHubInstallation Installation { get; init; }
    public required GitHubRepoInfo Repo { get; init; }
    public IReadOnlyList<GitHubBranchInfo> Branches { get; init; } = [];
    public IReadOnlyList<GitHubPullRequestInfo> OpenPullRequests { get; init; } = [];
    public IReadOnlyList<GitHubPullRequestInfo> MergedPullRequests { get; init; } = [];
    public IReadOnlyList<GitHubPullRequestInfo> ClosedPullRequests { get; init; } = [];
    public IReadOnlyList<GitHubCommitInfo> Commits { get; init; } = [];
    public string CommitsBranch { get; init; } = "main";
    public bool CanManage { get; init; }
    public GitHubRepoRules Rules { get; init; } = new()
    {
        Id = "",
        InstallationId = "",
        Owner = "",
        Repo = "",
    };
}

public sealed class GitHubRepoRulesInput
{
    public string InstallationId { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public bool AllowCreateBranch { get; set; } = true;
    public bool AllowMergeBranches { get; set; } = true;
    public bool AllowDeleteBranch { get; set; } = true;
    public string AllowedCreateFromBranches { get; set; } = "";
    public string AllowedMergeIntoBranches { get; set; } = "";
    public string ForbiddenMergeIntoBranches { get; set; } = "";
    public string ProtectedFromDeleteBranches { get; set; } = "";
}

public sealed class GitHubSetupDoneViewModel
{
    public required string AccountLogin { get; init; }
    public required string RedirectUrl { get; init; }
    public required string Message { get; init; }
}
