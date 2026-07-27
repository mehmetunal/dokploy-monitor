using DokployMonitor.Core.GitHub;

namespace DokployMonitor.Tests;

public sealed class GitHubBranchRuleEvaluatorTests
{
    private static GitHubRepoRules Rules(
        bool allowCreate = true,
        bool allowMerge = true,
        bool allowDelete = true,
        string createFrom = "",
        string mergeInto = "",
        string forbidden = "",
        string protectedDelete = "") =>
        new()
        {
            Id = "t",
            InstallationId = "i",
            Owner = "o",
            Repo = "r",
            AllowCreateBranch = allowCreate,
            AllowMergeBranches = allowMerge,
            AllowDeleteBranch = allowDelete,
            AllowedCreateFromBranches = createFrom,
            AllowedMergeIntoBranches = mergeInto,
            ForbiddenMergeIntoBranches = forbidden,
            ProtectedFromDeleteBranches = protectedDelete,
        };

    [Fact]
    public void ParseList_satir_ve_virgul_ayristirir()
    {
        var list = GitHubBranchRuleEvaluator.ParseList("main\ndevelop, staging; main");
        Assert.Equal(["main", "develop", "staging"], list);
    }

    [Fact]
    public void Create_sadece_izinli_kaynaktan()
    {
        var rules = Rules(createFrom: "main\ndevelop");
        Assert.Null(GitHubBranchRuleEvaluator.ValidateCreate(rules, "main", "feature/x"));
        Assert.NotNull(GitHubBranchRuleEvaluator.ValidateCreate(rules, "hotfix", "feature/x"));
    }

    [Fact]
    public void Create_kapaliysa_reddedilir()
    {
        Assert.NotNull(GitHubBranchRuleEvaluator.ValidateCreate(Rules(allowCreate: false), "main", "x"));
    }

    [Fact]
    public void Merge_yasakli_hedefe_izin_vermez()
    {
        var rules = Rules(forbidden: "main");
        Assert.NotNull(GitHubBranchRuleEvaluator.ValidateMerge(rules, "main", "feature"));
        Assert.Null(GitHubBranchRuleEvaluator.ValidateMerge(rules, "develop", "feature"));
    }

    [Fact]
    public void Merge_whitelist_disina_izin_vermez()
    {
        var rules = Rules(mergeInto: "develop\nstaging");
        Assert.Null(GitHubBranchRuleEvaluator.ValidateMerge(rules, "develop", "feature"));
        Assert.NotNull(GitHubBranchRuleEvaluator.ValidateMerge(rules, "main", "feature"));
    }

    [Fact]
    public void Delete_korumali_branch_silinemez()
    {
        var rules = Rules(protectedDelete: "release");
        Assert.NotNull(GitHubBranchRuleEvaluator.ValidateDelete(rules, "release", "main"));
        Assert.Null(GitHubBranchRuleEvaluator.ValidateDelete(rules, "feature", "main"));
        Assert.NotNull(GitHubBranchRuleEvaluator.ValidateDelete(rules, "main", "main"));
    }
}
