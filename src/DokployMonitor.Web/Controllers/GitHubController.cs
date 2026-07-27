using System.Security.Cryptography;
using System.Text.Json;
using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.GitHub;
using DokployMonitor.Infrastructure.Identity;
using DokployMonitor.Infrastructure.Localization;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace DokployMonitor.Web.Controllers;

/// <summary>
/// GitHub App Manifest + Install akisi. Kullanici API anahtari girmez;
/// "Create GitHub App" / "Install App" ile yetki GitHub uzerinden alinir.
/// </summary>
[Authorize]
public sealed class GitHubController(
    MonitorDbContext db,
    IGitHubAppClient github,
    IStringLocalizer<SharedResource> L,
    ILogger<GitHubController> logger) : Controller
{
    private const string ManifestStateCookie = "dm.github.manifest.state";

    public async Task<IActionResult> Index(string? installation, string? org, string? q, CancellationToken ct)
    {
        var app = await db.GitHubApps.OrderBy(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        var baseUrl = PublicBaseUrl();
        var canManage = User.IsInRole(MonitorRoles.SuperAdmin);

        if (app is null)
        {
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            Response.Cookies.Append(ManifestStateCookie, state, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                MaxAge = TimeSpan.FromHours(1),
            });

            return View(new GitHubIndexViewModel
            {
                PublicBaseUrl = baseUrl,
                ManifestJson = BuildManifestJson(baseUrl),
                ManifestState = state,
                Organization = string.IsNullOrWhiteSpace(org) ? null : org.Trim(),
                CanManage = canManage,
            });
        }

        var installations = await db.GitHubInstallations
            .Where(i => i.AppRegistrationId == app.Id)
            .OrderBy(i => i.AccountLogin)
            .ToListAsync(ct);

        var selected = installations.FirstOrDefault(i => i.Id == installation)
                       ?? installations.FirstOrDefault();

        IReadOnlyList<GitHubRepoInfo> repos = [];
        if (selected is not null)
        {
            try
            {
                repos = await github.ListInstallationRepositoriesAsync(app, selected.InstallationId, ct);
                selected.LastSyncedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "GitHub repo listesi alinamadi ({Account})", selected.AccountLogin);
                TempData["Error"] = ex.Message;
            }
        }

        var query = q?.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            repos = repos
                .Where(repo =>
                    repo.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || repo.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || repo.OwnerLogin.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (repo.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        var installationViews = installations.Select(i => new GitHubInstallationViewModel
        {
            Id = i.Id,
            InstallationId = i.InstallationId,
            AccountLogin = i.AccountLogin,
            AccountType = i.AccountType,
            AccountAvatarUrl = i.AccountAvatarUrl,
        }).ToList();

        return View(new GitHubIndexViewModel
        {
            App = app,
            Installations = installationViews,
            SelectedInstallationId = selected?.Id,
            SelectedInstallation = installationViews.FirstOrDefault(i => i.Id == selected?.Id),
            Repositories = repos,
            RepoQuery = query,
            PublicBaseUrl = baseUrl,
            CanManage = canManage,
        });
    }

    /// <summary>Manifest kaydi sonrasi GitHub'in yonlendirdigi callback.</summary>
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> ManifestCallback(string? code, string? state, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            TempData["Error"] = L["GitHub App could not be created: missing code."].Value;
            return RedirectToAction(nameof(Index));
        }

        var expectedState = Request.Cookies[ManifestStateCookie];
        Response.Cookies.Delete(ManifestStateCookie);

        if (string.IsNullOrWhiteSpace(expectedState)
            || !string.Equals(expectedState, state, StringComparison.Ordinal))
        {
            TempData["Error"] = L["GitHub App creation failed: invalid state."].Value;
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var converted = await github.ConvertManifestAsync(code, ct);

            // Tek App kaydi: eskiyi silip yenisini yaz (kurulumlar cascade ile gider).
            var existing = await db.GitHubApps.ToListAsync(ct);
            db.GitHubApps.RemoveRange(existing);

            db.GitHubApps.Add(new GitHubAppRegistration
            {
                Id = Guid.NewGuid().ToString("n"),
                AppId = converted.AppId,
                ClientId = converted.ClientId,
                ClientSecret = converted.ClientSecret,
                PrivateKeyPem = converted.PrivateKeyPem,
                WebhookSecret = converted.WebhookSecret,
                Slug = converted.Slug,
                Name = converted.Name,
                HtmlUrl = converted.HtmlUrl,
                OwnerLogin = converted.OwnerLogin,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "GitHub App Manifest ile olusturuldu: {Slug} (islem: {Actor})",
                converted.Slug,
                User.Identity?.Name);

            TempData["Message"] = L["GitHub App '{0}' created. Now install it on an account or organization.", converted.Name].Value;

            return Redirect($"https://github.com/apps/{Uri.EscapeDataString(converted.Slug)}/installations/new");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GitHub Manifest donusumu basarisiz");
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>Install App sonrasi setup_url; installation_id kaydedilir.</summary>
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> Setup(long? installation_id, CancellationToken ct)
    {
        if (installation_id is null or <= 0)
        {
            TempData["Error"] = L["GitHub installation id is missing."].Value;
            return RedirectToAction(nameof(Index));
        }

        var app = await db.GitHubApps.OrderBy(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        if (app is null)
        {
            TempData["Error"] = L["Create the GitHub App first."].Value;
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var remote = await github.ListAppInstallationsAsync(app, ct);
            var match = remote.FirstOrDefault(i => i.InstallationId == installation_id.Value);
            if (match is null)
            {
                TempData["Error"] = L["Installation not found for this GitHub App."].Value;
                return RedirectToAction(nameof(Index));
            }

            var existing = await db.GitHubInstallations
                .FirstOrDefaultAsync(i => i.InstallationId == match.InstallationId, ct);

            if (existing is null)
            {
                existing = new GitHubInstallation
                {
                    Id = Guid.NewGuid().ToString("n"),
                    AppRegistrationId = app.Id,
                    InstallationId = match.InstallationId,
                    AccountLogin = match.AccountLogin,
                    AccountType = match.AccountType,
                    AccountAvatarUrl = match.AccountAvatarUrl,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastSyncedAt = DateTimeOffset.UtcNow,
                };
                db.GitHubInstallations.Add(existing);
            }
            else
            {
                existing.AccountLogin = match.AccountLogin;
                existing.AccountType = match.AccountType;
                existing.AccountAvatarUrl = match.AccountAvatarUrl;
                existing.AppRegistrationId = app.Id;
                existing.LastSyncedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);

            var redirectUrl = Url.Action(nameof(Index), new { installation = existing.Id }) ?? "/GitHub";
            return View("SetupDone", new GitHubSetupDoneViewModel
            {
                AccountLogin = match.AccountLogin,
                RedirectUrl = redirectUrl,
                Message = L["GitHub connected: {0}", match.AccountLogin].Value,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GitHub Setup basarisiz");
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> SyncInstallations(CancellationToken ct)
    {
        var app = await db.GitHubApps.OrderBy(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        if (app is null)
        {
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var remote = await github.ListAppInstallationsAsync(app, ct);
            var local = await db.GitHubInstallations
                .Where(i => i.AppRegistrationId == app.Id)
                .ToListAsync(ct);

            foreach (var item in remote)
            {
                var row = local.FirstOrDefault(i => i.InstallationId == item.InstallationId);
                if (row is null)
                {
                    db.GitHubInstallations.Add(new GitHubInstallation
                    {
                        Id = Guid.NewGuid().ToString("n"),
                        AppRegistrationId = app.Id,
                        InstallationId = item.InstallationId,
                        AccountLogin = item.AccountLogin,
                        AccountType = item.AccountType,
                        AccountAvatarUrl = item.AccountAvatarUrl,
                        CreatedAt = DateTimeOffset.UtcNow,
                        LastSyncedAt = DateTimeOffset.UtcNow,
                    });
                }
                else
                {
                    row.AccountLogin = item.AccountLogin;
                    row.AccountType = item.AccountType;
                    row.AccountAvatarUrl = item.AccountAvatarUrl;
                    row.LastSyncedAt = DateTimeOffset.UtcNow;
                }
            }

            var remoteIds = remote.Select(r => r.InstallationId).ToHashSet();
            db.GitHubInstallations.RemoveRange(local.Where(i => !remoteIds.Contains(i.InstallationId)));

            await db.SaveChangesAsync(ct);
            TempData["Message"] = L["Installations refreshed."].Value;
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var apps = await db.GitHubApps.ToListAsync(ct);
        db.GitHubApps.RemoveRange(apps);
        await db.SaveChangesAsync(ct);

        TempData["Message"] = L["GitHub App connection removed from Monitor. You can also uninstall the app on GitHub."].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Repo(
        string installationId,
        string owner,
        string name,
        string? commitsBranch,
        CancellationToken ct)
    {
        var context = await ResolveRepoContextAsync(installationId, owner, name, ct);
        if (context is null)
        {
            return NotFound();
        }

        try
        {
            var branchName = string.IsNullOrWhiteSpace(commitsBranch)
                ? context.Repo.DefaultBranch
                : commitsBranch.Trim();

            var branchesTask = github.ListBranchesAsync(
                context.App, context.Installation.InstallationId, owner, name, ct);
            var prsTask = github.ListPullRequestsAsync(
                context.App, context.Installation.InstallationId, owner, name, "all", 2, ct);
            var commitsTask = github.ListCommitsAsync(
                context.App, context.Installation.InstallationId, owner, name, branchName, 30, ct);

            await Task.WhenAll(branchesTask, prsTask, commitsTask);

            var allPrs = await prsTask;
            var rules = await GetOrDefaultRulesAsync(installationId, owner, name, ct);
            return View(new GitHubRepoViewModel
            {
                App = context.App,
                Installation = context.Installation,
                Repo = context.Repo,
                Branches = await branchesTask,
                OpenPullRequests = allPrs.Where(pr => pr.State == "open").ToList(),
                MergedPullRequests = allPrs.Where(pr => pr.Merged).ToList(),
                ClosedPullRequests = allPrs.Where(pr => pr.State == "closed" && !pr.Merged).ToList(),
                Commits = await commitsTask,
                CommitsBranch = branchName,
                CanManage = User.IsInRole(MonitorRoles.SuperAdmin),
                Rules = rules,
            });
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index), new { installation = installationId });
        }
    }

    /// <summary>Repo sayfasi canli yenileme: branch, PR (open/merged/closed) + commit gecmisi.</summary>
    [HttpGet]
    public async Task<IActionResult> RepoSnapshot(
        string installationId,
        string owner,
        string name,
        string? commitsBranch,
        CancellationToken ct)
    {
        var pair = await ResolveInstallationAsync(installationId, ct);
        if (pair is null)
        {
            return NotFound();
        }

        var (app, installation) = pair.Value;
        var branchName = string.IsNullOrWhiteSpace(commitsBranch) ? null : commitsBranch.Trim();

        try
        {
            var branchesTask = github.ListBranchesAsync(app, installation.InstallationId, owner, name, ct);
            var prsTask = github.ListPullRequestsAsync(app, installation.InstallationId, owner, name, "all", 2, ct);

            // commitsBranch bos ise once branch listesi gelmeden default bilinmez;
            // commits'i branches ile paralel cekmek icin sha parametresi opsiyonel kalir.
            var commitsTask = github.ListCommitsAsync(
                app, installation.InstallationId, owner, name, branchName, 30, ct);

            await Task.WhenAll(branchesTask, prsTask, commitsTask);

            var branches = await branchesTask;
            var allPrs = await prsTask;
            var commits = await commitsTask;
            var resolvedBranch = branchName
                ?? branches.FirstOrDefault()?.Name
                ?? "main";

            var rules = await GetOrDefaultRulesAsync(installationId, owner, name, ct);

            return Json(new
            {
                canManage = User.IsInRole(MonitorRoles.SuperAdmin),
                fetchedAt = DateTimeOffset.UtcNow,
                commitsBranch = resolvedBranch,
                rules = new
                {
                    allowCreateBranch = rules.AllowCreateBranch,
                    allowMergeBranches = rules.AllowMergeBranches,
                    allowDeleteBranch = rules.AllowDeleteBranch,
                    allowedCreateFromBranches = GitHubBranchRuleEvaluator.ParseList(rules.AllowedCreateFromBranches),
                    allowedMergeIntoBranches = GitHubBranchRuleEvaluator.ParseList(rules.AllowedMergeIntoBranches),
                    forbiddenMergeIntoBranches = GitHubBranchRuleEvaluator.ParseList(rules.ForbiddenMergeIntoBranches),
                    protectedFromDeleteBranches = GitHubBranchRuleEvaluator.ParseList(rules.ProtectedFromDeleteBranches),
                },
                branches = branches.Select(b => new
                {
                    name = b.Name,
                    sha = b.Sha,
                    isProtected = b.Protected,
                }),
                openPullRequests = allPrs.Where(pr => pr.State == "open").Select(ToPrJson),
                mergedPullRequests = allPrs.Where(pr => pr.Merged).Select(ToPrJson),
                closedPullRequests = allPrs.Where(pr => pr.State == "closed" && !pr.Merged).Select(ToPrJson),
                commits = commits.Select(c => new
                {
                    sha = c.Sha,
                    message = c.Message,
                    authorName = c.AuthorName,
                    authorLogin = c.AuthorLogin,
                    committedAt = c.CommittedAt,
                    htmlUrl = c.HtmlUrl,
                }),
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GitHub RepoSnapshot basarisiz ({Owner}/{Name})", owner, name);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> SaveRepoRules(GitHubRepoRulesInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.InstallationId)
            || string.IsNullOrWhiteSpace(input.Owner)
            || string.IsNullOrWhiteSpace(input.Name))
        {
            return BadRequest();
        }

        var installation = await db.GitHubInstallations
            .FirstOrDefaultAsync(i => i.Id == input.InstallationId, ct);
        if (installation is null)
        {
            return NotFound();
        }

        var existing = await db.GitHubRepoRules.FirstOrDefaultAsync(
            r => r.InstallationId == input.InstallationId
                 && r.Owner == input.Owner
                 && r.Repo == input.Name,
            ct);

        if (existing is null)
        {
            existing = new GitHubRepoRules
            {
                Id = Guid.NewGuid().ToString("n"),
                InstallationId = input.InstallationId,
                Owner = input.Owner.Trim(),
                Repo = input.Name.Trim(),
            };
            db.GitHubRepoRules.Add(existing);
        }

        existing.AllowCreateBranch = input.AllowCreateBranch;
        existing.AllowMergeBranches = input.AllowMergeBranches;
        existing.AllowDeleteBranch = input.AllowDeleteBranch;
        existing.AllowedCreateFromBranches = NormalizeBranchList(input.AllowedCreateFromBranches);
        existing.AllowedMergeIntoBranches = NormalizeBranchList(input.AllowedMergeIntoBranches);
        existing.ForbiddenMergeIntoBranches = NormalizeBranchList(input.ForbiddenMergeIntoBranches);
        existing.ProtectedFromDeleteBranches = NormalizeBranchList(input.ProtectedFromDeleteBranches);
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        TempData["Message"] = L["Branch rules saved for {0}/{1}.", input.Owner, input.Name].Value;
        return RedirectToAction(nameof(Repo), new
        {
            installationId = input.InstallationId,
            owner = input.Owner,
            name = input.Name,
        });
    }

    private static object ToPrJson(GitHubPullRequestInfo pr) => new
    {
        number = pr.Number,
        title = pr.Title,
        state = pr.State,
        headRef = pr.HeadRef,
        baseRef = pr.BaseRef,
        htmlUrl = pr.HtmlUrl,
        userLogin = pr.UserLogin,
        draft = pr.Draft,
        merged = pr.Merged,
        updatedAt = pr.UpdatedAt,
        mergedAt = pr.MergedAt,
        closedAt = pr.ClosedAt,
    };

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> CreateBranch(
        string installationId,
        string owner,
        string name,
        string newBranch,
        string fromBranch,
        CancellationToken ct)
    {
        var context = await ResolveRepoContextAsync(installationId, owner, name, ct);
        if (context is null)
        {
            return NotFound();
        }

        var rules = await FindRulesAsync(installationId, owner, name, ct);
        var ruleError = GitHubBranchRuleEvaluator.ValidateCreate(rules, fromBranch.Trim(), newBranch.Trim());
        if (ruleError is not null)
        {
            TempData["Error"] = L[ruleError].Value;
            return RedirectToAction(nameof(Repo), new { installationId, owner, name });
        }

        try
        {
            await github.CreateBranchAsync(
                context.App,
                context.Installation.InstallationId,
                owner,
                name,
                newBranch.Trim(),
                fromBranch.Trim(),
                ct);

            TempData["Message"] = L["Branch '{0}' created from '{1}'.", newBranch, fromBranch].Value;
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Repo), new { installationId, owner, name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> DeleteBranch(
        string installationId,
        string owner,
        string name,
        string branch,
        CancellationToken ct)
    {
        var context = await ResolveRepoContextAsync(installationId, owner, name, ct);
        if (context is null)
        {
            return NotFound();
        }

        var rules = await FindRulesAsync(installationId, owner, name, ct);
        var ruleError = GitHubBranchRuleEvaluator.ValidateDelete(rules, branch, context.Repo.DefaultBranch);
        if (ruleError is not null)
        {
            TempData["Error"] = L[ruleError].Value;
            return RedirectToAction(nameof(Repo), new { installationId, owner, name });
        }

        try
        {
            await github.DeleteBranchAsync(
                context.App, context.Installation.InstallationId, owner, name, branch, ct);
            TempData["Message"] = L["Branch '{0}' deleted.", branch].Value;
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Repo), new { installationId, owner, name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> MergeBranches(
        string installationId,
        string owner,
        string name,
        string baseBranch,
        string headBranch,
        string? commitMessage,
        CancellationToken ct)
    {
        var context = await ResolveRepoContextAsync(installationId, owner, name, ct);
        if (context is null)
        {
            return NotFound();
        }

        var rules = await FindRulesAsync(installationId, owner, name, ct);
        var ruleError = GitHubBranchRuleEvaluator.ValidateMerge(rules, baseBranch.Trim(), headBranch.Trim());
        if (ruleError is not null)
        {
            TempData["Error"] = L[ruleError].Value;
            return RedirectToAction(nameof(Repo), new { installationId, owner, name });
        }

        try
        {
            await github.MergeBranchesAsync(
                context.App,
                context.Installation.InstallationId,
                owner,
                name,
                baseBranch.Trim(),
                headBranch.Trim(),
                commitMessage,
                ct);

            TempData["Message"] = L["Merged '{0}' into '{1}'.", headBranch, baseBranch].Value;
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Repo), new { installationId, owner, name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> MergePullRequest(
        string installationId,
        string owner,
        string name,
        int number,
        CancellationToken ct)
    {
        var context = await ResolveRepoContextAsync(installationId, owner, name, ct);
        if (context is null)
        {
            return NotFound();
        }

        try
        {
            var prs = await github.ListPullRequestsAsync(
                context.App, context.Installation.InstallationId, owner, name, "open", 2, ct);
            var pr = prs.FirstOrDefault(p => p.Number == number);
            if (pr is null)
            {
                TempData["Error"] = L["Pull request #{0} was not found or is not open.", number].Value;
                return RedirectToAction(nameof(Repo), new { installationId, owner, name });
            }

            var rules = await FindRulesAsync(installationId, owner, name, ct);
            var ruleError = GitHubBranchRuleEvaluator.ValidateMerge(rules, pr.BaseRef, pr.HeadRef);
            if (ruleError is not null)
            {
                TempData["Error"] = L[ruleError].Value;
                return RedirectToAction(nameof(Repo), new { installationId, owner, name });
            }

            await github.MergePullRequestAsync(
                context.App, context.Installation.InstallationId, owner, name, number, null, ct);
            TempData["Message"] = L["Pull request #{0} merged.", number].Value;
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Repo), new { installationId, owner, name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> ApprovePullRequest(
        string installationId,
        string owner,
        string name,
        int number,
        CancellationToken ct)
    {
        var context = await ResolveRepoContextAsync(installationId, owner, name, ct);
        if (context is null)
        {
            return NotFound();
        }

        try
        {
            await github.ReviewPullRequestAsync(
                context.App,
                context.Installation.InstallationId,
                owner,
                name,
                number,
                "APPROVE",
                null,
                ct);

            TempData["Message"] = L["Pull request #{0} approved.", number].Value;
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Repo), new { installationId, owner, name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> RejectPullRequest(
        string installationId,
        string owner,
        string name,
        int number,
        CancellationToken ct)
    {
        var context = await ResolveRepoContextAsync(installationId, owner, name, ct);
        if (context is null)
        {
            return NotFound();
        }

        try
        {
            await github.ReviewPullRequestAsync(
                context.App,
                context.Installation.InstallationId,
                owner,
                name,
                number,
                "REQUEST_CHANGES",
                "Rejected via Dokploy Monitor.",
                ct);

            TempData["Message"] = L["Pull request #{0} changes requested.", number].Value;
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Repo), new { installationId, owner, name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = MonitorRoles.SuperAdmin)]
    public async Task<IActionResult> ClosePullRequest(
        string installationId,
        string owner,
        string name,
        int number,
        CancellationToken ct)
    {
        var context = await ResolveRepoContextAsync(installationId, owner, name, ct);
        if (context is null)
        {
            return NotFound();
        }

        try
        {
            await github.ClosePullRequestAsync(
                context.App, context.Installation.InstallationId, owner, name, number, ct);
            TempData["Message"] = L["Pull request #{0} closed.", number].Value;
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Repo), new { installationId, owner, name });
    }

    /// <summary>Manifest icin zorunlu webhook URL; olaylar su an islenmez.</summary>
    [AllowAnonymous]
    [HttpPost("/api/webhooks/github")]
    [IgnoreAntiforgeryToken]
    public IActionResult Webhook() => Ok();

    private sealed record RepoContext(
        GitHubAppRegistration App,
        GitHubInstallation Installation,
        GitHubRepoInfo Repo);

    private async Task<(GitHubAppRegistration App, GitHubInstallation Installation)?> ResolveInstallationAsync(
        string installationId,
        CancellationToken ct)
    {
        var installation = await db.GitHubInstallations.FirstOrDefaultAsync(i => i.Id == installationId, ct);
        if (installation is null)
        {
            return null;
        }

        var app = await db.GitHubApps.FirstOrDefaultAsync(a => a.Id == installation.AppRegistrationId, ct);
        return app is null ? null : (app, installation);
    }

    private async Task<RepoContext?> ResolveRepoContextAsync(
        string installationId,
        string owner,
        string name,
        CancellationToken ct)
    {
        var pair = await ResolveInstallationAsync(installationId, ct);
        if (pair is null)
        {
            return null;
        }

        var (app, installation) = pair.Value;
        var repos = await github.ListInstallationRepositoriesAsync(app, installation.InstallationId, ct);
        var repo = repos.FirstOrDefault(r =>
            string.Equals(r.OwnerLogin, owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

        return repo is null ? null : new RepoContext(app, installation, repo);
    }

    private Task<GitHubRepoRules?> FindRulesAsync(
        string installationId,
        string owner,
        string repo,
        CancellationToken ct) =>
        db.GitHubRepoRules.FirstOrDefaultAsync(
            r => r.InstallationId == installationId
                 && r.Owner == owner
                 && r.Repo == repo,
            ct);

    private async Task<GitHubRepoRules> GetOrDefaultRulesAsync(
        string installationId,
        string owner,
        string repo,
        CancellationToken ct)
    {
        var existing = await FindRulesAsync(installationId, owner, repo, ct);
        return existing ?? new GitHubRepoRules
        {
            Id = "",
            InstallationId = installationId,
            Owner = owner,
            Repo = repo,
            AllowCreateBranch = true,
            AllowMergeBranches = true,
            AllowDeleteBranch = true,
            AllowedCreateFromBranches = "",
            AllowedMergeIntoBranches = "",
            ForbiddenMergeIntoBranches = "",
            ProtectedFromDeleteBranches = "",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static string NormalizeBranchList(string? raw) =>
        string.Join('\n', GitHubBranchRuleEvaluator.ParseList(raw));

    private string PublicBaseUrl()
    {
        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');
    }

    private static string BuildManifestJson(string baseUrl)
    {
        // hook_attributes bilerek yok: GitHub localhost webhook URL'ini reddeder.
        // Repo/branch/PR yonetimi webhook gerektirmez; event dinlemiyoruz.
        var manifest = new Dictionary<string, object?>
        {
            ["name"] = "Trimango Dokploy Monitor",
            ["url"] = baseUrl,
            ["redirect_url"] = $"{baseUrl}/GitHub/ManifestCallback",
            ["callback_urls"] = new[] { $"{baseUrl}/GitHub/ManifestCallback" },
            ["setup_url"] = $"{baseUrl}/GitHub/Setup",
            ["description"] = "Manage repositories, branches and pull requests from Dokploy Monitor.",
            ["public"] = false,
            ["default_permissions"] = new Dictionary<string, string>
            {
                ["contents"] = "write",
                ["metadata"] = "read",
                ["pull_requests"] = "write",
            },
            ["request_oauth_on_install"] = false,
            ["setup_on_update"] = true,
        };

        return JsonSerializer.Serialize(manifest);
    }
}
