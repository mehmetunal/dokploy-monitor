namespace DokployMonitor.Core.GitHub;

/// <summary>Branch kurali listelerini parse eder ve create/merge/delete icin dogrular.</summary>
public static class GitHubBranchRuleEvaluator
{
    public static IReadOnlyList<string> ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsMatch(IReadOnlyList<string> patterns, string branch) =>
        patterns.Any(pattern => string.Equals(pattern, branch, StringComparison.OrdinalIgnoreCase));

    public static string? ValidateCreate(GitHubRepoRules? rules, string fromBranch, string newBranch)
    {
        if (rules is { AllowCreateBranch: false })
        {
            return "Branch creation is disabled for this repository.";
        }

        if (string.IsNullOrWhiteSpace(newBranch))
        {
            return "New branch name is required.";
        }

        var allowedFrom = ParseList(rules?.AllowedCreateFromBranches);
        if (allowedFrom.Count > 0 && !IsMatch(allowedFrom, fromBranch))
        {
            return $"Branches may only be created from: {string.Join(", ", allowedFrom)}.";
        }

        return null;
    }

    public static string? ValidateMerge(GitHubRepoRules? rules, string baseBranch, string headBranch)
    {
        if (rules is { AllowMergeBranches: false })
        {
            return "Branch merge is disabled for this repository.";
        }

        if (string.Equals(baseBranch, headBranch, StringComparison.OrdinalIgnoreCase))
        {
            return "Base and head branches must be different.";
        }

        var forbidden = ParseList(rules?.ForbiddenMergeIntoBranches);
        if (IsMatch(forbidden, baseBranch))
        {
            return $"Merging into '{baseBranch}' is forbidden by repository rules.";
        }

        var allowedInto = ParseList(rules?.AllowedMergeIntoBranches);
        if (allowedInto.Count > 0 && !IsMatch(allowedInto, baseBranch))
        {
            return $"Merges are only allowed into: {string.Join(", ", allowedInto)}.";
        }

        return null;
    }

    public static string? ValidateDelete(GitHubRepoRules? rules, string branch, string defaultBranch)
    {
        if (rules is { AllowDeleteBranch: false })
        {
            return "Branch deletion is disabled for this repository.";
        }

        if (string.Equals(branch, defaultBranch, StringComparison.OrdinalIgnoreCase))
        {
            return "Default branch cannot be deleted.";
        }

        var protectedBranches = ParseList(rules?.ProtectedFromDeleteBranches);
        if (IsMatch(protectedBranches, branch))
        {
            return $"Branch '{branch}' is protected from deletion by repository rules.";
        }

        return null;
    }
}
