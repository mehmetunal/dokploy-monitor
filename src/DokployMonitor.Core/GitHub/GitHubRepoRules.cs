namespace DokployMonitor.Core.GitHub;

/// <summary>
/// Panel uzerinden branch create/merge/delete icin repo bazli kurallar.
/// Bos liste = kisit yok (ilgili alan icin).
/// </summary>
public class GitHubRepoRules
{
    public required string Id { get; set; }

    /// <summary>Monitor'deki GitHubInstallation.Id</summary>
    public required string InstallationId { get; set; }

    public required string Owner { get; set; }

    public required string Repo { get; set; }

    public bool AllowCreateBranch { get; set; } = true;

    public bool AllowMergeBranches { get; set; } = true;

    public bool AllowDeleteBranch { get; set; } = true;

    /// <summary>
    /// Branch olustururken izin verilen kaynak branch'ler (satir veya virgul).
    /// Bos = tum branch'lerden olusturulabilir.
    /// </summary>
    public string AllowedCreateFromBranches { get; set; } = "";

    /// <summary>
    /// Merge hedefi olabilecek branch'ler (whitelist). Bos = Forbidden listesi disinda hepsi.
    /// </summary>
    public string AllowedMergeIntoBranches { get; set; } = "";

    /// <summary>Merge yapilamayacak hedef branch'ler (blacklist).</summary>
    public string ForbiddenMergeIntoBranches { get; set; } = "";

    /// <summary>Panelden silinemeyecek branch'ler (default branch zaten korunur).</summary>
    public string ProtectedFromDeleteBranches { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}
