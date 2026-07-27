using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DokployMonitor.Core.Abstractions;
using DokployMonitor.Core.GitHub;

namespace DokployMonitor.Infrastructure.GitHub;

/// <summary>
/// GitHub REST API istemcisi. Kimlik: App JWT → installation access token.
/// Kullaniciya API anahtari sorulmaz; Manifest/Install akisi yeterlidir.
/// </summary>
public sealed class GitHubAppClient(IHttpClientFactory httpClientFactory) : IGitHubAppClient
{
    public const string HttpClientName = "GitHubApi";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<GitHubManifestConversionResult> ConvertManifestAsync(
        string code,
        CancellationToken ct = default)
    {
        var client = CreateAnonymousClient();
        using var response = await client.PostAsync($"app-manifests/{code}/conversions", content: null, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub Manifest donusumu basarisiz ({(int)response.StatusCode}): {Truncate(body)}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        return new GitHubManifestConversionResult(
            AppId: root.GetProperty("id").GetInt64(),
            ClientId: root.GetProperty("client_id").GetString()
                ?? throw new InvalidOperationException("GitHub Manifest yanitinda client_id yok."),
            ClientSecret: root.GetProperty("client_secret").GetString()
                ?? throw new InvalidOperationException("GitHub Manifest yanitinda client_secret yok."),
            PrivateKeyPem: root.GetProperty("pem").GetString()
                ?? throw new InvalidOperationException("GitHub Manifest yanitinda pem yok."),
            WebhookSecret: root.TryGetProperty("webhook_secret", out var secret)
                ? secret.GetString()
                : null,
            Slug: root.GetProperty("slug").GetString()
                ?? throw new InvalidOperationException("GitHub Manifest yanitinda slug yok."),
            Name: root.GetProperty("name").GetString() ?? "Dokploy Monitor",
            HtmlUrl: root.TryGetProperty("html_url", out var html) ? html.GetString() : null,
            OwnerLogin: root.TryGetProperty("owner", out var owner)
                && owner.ValueKind == JsonValueKind.Object
                && owner.TryGetProperty("login", out var login)
                    ? login.GetString()
                    : null);
    }

    public async Task<IReadOnlyList<GitHubInstallationInfo>> ListAppInstallationsAsync(
        GitHubAppRegistration app,
        CancellationToken ct = default)
    {
        var client = await CreateAppClientAsync(app, ct);
        var results = new List<GitHubInstallationInfo>();
        var url = "app/installations?per_page=100";

        while (!string.IsNullOrEmpty(url))
        {
            using var response = await client.GetAsync(url, ct);
            await EnsureSuccessAsync(response, "kurulumlar listelenemedi", ct);

            var page = await response.Content.ReadFromJsonAsync<List<InstallationDto>>(JsonOptions, ct)
                       ?? [];

            foreach (var item in page)
            {
                if (item.Account is null)
                {
                    continue;
                }

                results.Add(new GitHubInstallationInfo(
                    item.Id,
                    item.Account.Login ?? "unknown",
                    item.Account.Type ?? "User",
                    item.Account.AvatarUrl));
            }

            url = GetNextLink(response.Headers);
        }

        return results;
    }

    public async Task<IReadOnlyList<GitHubRepoInfo>> ListInstallationRepositoriesAsync(
        GitHubAppRegistration app,
        long installationId,
        CancellationToken ct = default)
    {
        var client = await CreateInstallationClientAsync(app, installationId, ct);
        var results = new List<GitHubRepoInfo>();
        var url = "installation/repositories?per_page=100";

        while (!string.IsNullOrEmpty(url))
        {
            using var response = await client.GetAsync(url, ct);
            await EnsureSuccessAsync(response, "repolar listelenemedi", ct);

            var payload = await response.Content.ReadFromJsonAsync<InstallationReposDto>(JsonOptions, ct);
            foreach (var repo in payload?.Repositories ?? [])
            {
                results.Add(ToRepoInfo(repo));
            }

            url = GetNextLink(response.Headers);
        }

        return results;
    }

    public async Task<IReadOnlyList<GitHubBranchInfo>> ListBranchesAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        CancellationToken ct = default)
    {
        var client = await CreateInstallationClientAsync(app, installationId, ct);
        var results = new List<GitHubBranchInfo>();
        var url = $"repos/{owner}/{repo}/branches?per_page=100";

        while (!string.IsNullOrEmpty(url))
        {
            using var response = await client.GetAsync(url, ct);
            await EnsureSuccessAsync(response, "branch'ler listelenemedi", ct);

            var page = await response.Content.ReadFromJsonAsync<List<BranchDto>>(JsonOptions, ct) ?? [];
            foreach (var branch in page)
            {
                results.Add(new GitHubBranchInfo(
                    branch.Name ?? "",
                    branch.Commit?.Sha ?? "",
                    branch.Protected));
            }

            url = GetNextLink(response.Headers);
        }

        return results;
    }

    public async Task<IReadOnlyList<GitHubPullRequestInfo>> ListPullRequestsAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        string state = "open",
        int maxPages = 2,
        CancellationToken ct = default)
    {
        var client = await CreateInstallationClientAsync(app, installationId, ct);
        var results = new List<GitHubPullRequestInfo>();
        var url = $"repos/{owner}/{repo}/pulls?state={Uri.EscapeDataString(state)}&per_page=50&sort=updated&direction=desc";
        var pages = 0;

        while (!string.IsNullOrEmpty(url) && pages < Math.Clamp(maxPages, 1, 10))
        {
            using var response = await client.GetAsync(url, ct);
            await EnsureSuccessAsync(response, "PR'lar listelenemedi", ct);
            pages++;

            var page = await response.Content.ReadFromJsonAsync<List<PullRequestDto>>(JsonOptions, ct) ?? [];
            foreach (var pr in page)
            {
                results.Add(new GitHubPullRequestInfo(
                    pr.Number,
                    pr.Title ?? "",
                    pr.State ?? "open",
                    pr.Head?.Ref ?? "",
                    pr.Base?.Ref ?? "",
                    pr.HtmlUrl ?? "",
                    pr.User?.Login ?? "",
                    pr.Draft,
                    pr.Mergeable ?? false,
                    pr.MergedAt is not null,
                    pr.CreatedAt,
                    pr.UpdatedAt,
                    pr.MergedAt,
                    pr.ClosedAt));
            }

            url = GetNextLink(response.Headers);
        }

        return results;
    }

    public async Task<IReadOnlyList<GitHubCommitInfo>> ListCommitsAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        string? sha = null,
        int perPage = 30,
        CancellationToken ct = default)
    {
        var client = await CreateInstallationClientAsync(app, installationId, ct);
        var limit = Math.Clamp(perPage, 1, 100);
        var url = $"repos/{owner}/{repo}/commits?per_page={limit}";
        if (!string.IsNullOrWhiteSpace(sha))
        {
            url += $"&sha={Uri.EscapeDataString(sha)}";
        }

        using var response = await client.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "commit'ler listelenemedi", ct);

        var page = await response.Content.ReadFromJsonAsync<List<CommitHistoryDto>>(JsonOptions, ct) ?? [];
        return page.Select(commit =>
        {
            var message = commit.Commit?.Message ?? "";
            var firstLine = message.Split('\n', 2, StringSplitOptions.None)[0];
            return new GitHubCommitInfo(
                commit.Sha ?? "",
                firstLine,
                commit.Commit?.Author?.Name
                    ?? commit.Commit?.Committer?.Name
                    ?? commit.Author?.Login
                    ?? "unknown",
                commit.Author?.Login,
                commit.Commit?.Author?.Date ?? commit.Commit?.Committer?.Date,
                commit.HtmlUrl ?? "");
        }).ToList();
    }

    public async Task CreateBranchAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        string newBranch,
        string fromBranch,
        CancellationToken ct = default)
    {
        var client = await CreateInstallationClientAsync(app, installationId, ct);

        using var refResponse = await client.GetAsync(
            $"repos/{owner}/{repo}/git/ref/heads/{Uri.EscapeDataString(fromBranch)}", ct);
        await EnsureSuccessAsync(refResponse, $"'{fromBranch}' branch'i bulunamadi", ct);

        var refDto = await refResponse.Content.ReadFromJsonAsync<GitRefDto>(JsonOptions, ct);
        var sha = refDto?.Object?.Sha
                  ?? throw new InvalidOperationException($"'{fromBranch}' icin SHA alinamadi.");

        using var createResponse = await client.PostAsJsonAsync(
            $"repos/{owner}/{repo}/git/refs",
            new { @ref = $"refs/heads/{newBranch}", sha },
            JsonOptions,
            ct);

        await EnsureSuccessAsync(createResponse, "branch olusturulamadi", ct);
    }

    public async Task DeleteBranchAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        string branch,
        CancellationToken ct = default)
    {
        var client = await CreateInstallationClientAsync(app, installationId, ct);
        using var response = await client.DeleteAsync(
            $"repos/{owner}/{repo}/git/refs/heads/{Uri.EscapeDataString(branch)}", ct);
        await EnsureSuccessAsync(response, "branch silinemedi", ct);
    }

    public async Task MergeBranchesAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        string baseBranch,
        string headBranch,
        string? commitMessage,
        CancellationToken ct = default)
    {
        var client = await CreateInstallationClientAsync(app, installationId, ct);
        using var response = await client.PostAsJsonAsync(
            $"repos/{owner}/{repo}/merges",
            new
            {
                @base = baseBranch,
                head = headBranch,
                commit_message = commitMessage
                    ?? $"Merge branch '{headBranch}' into {baseBranch}",
            },
            JsonOptions,
            ct);

        await EnsureSuccessAsync(response, "branch merge basarisiz", ct);
    }

    public async Task MergePullRequestAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        int pullNumber,
        string? commitTitle,
        CancellationToken ct = default)
    {
        var client = await CreateInstallationClientAsync(app, installationId, ct);
        using var response = await client.PutAsJsonAsync(
            $"repos/{owner}/{repo}/pulls/{pullNumber}/merge",
            new
            {
                commit_title = commitTitle,
                merge_method = "merge",
            },
            JsonOptions,
            ct);

        await EnsureSuccessAsync(response, $"PR #{pullNumber} merge edilemedi", ct);
    }

    public async Task ReviewPullRequestAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        int pullNumber,
        string eventType,
        string? body,
        CancellationToken ct = default)
    {
        var client = await CreateInstallationClientAsync(app, installationId, ct);
        using var response = await client.PostAsJsonAsync(
            $"repos/{owner}/{repo}/pulls/{pullNumber}/reviews",
            new
            {
                body = body ?? (eventType == "APPROVE" ? "Approved via Dokploy Monitor." : "Changes requested via Dokploy Monitor."),
                @event = eventType,
            },
            JsonOptions,
            ct);

        await EnsureSuccessAsync(response, $"PR #{pullNumber} incelemesi gonderilemedi", ct);
    }

    public async Task ClosePullRequestAsync(
        GitHubAppRegistration app,
        long installationId,
        string owner,
        string repo,
        int pullNumber,
        CancellationToken ct = default)
    {
        var client = await CreateInstallationClientAsync(app, installationId, ct);
        using var response = await client.PatchAsJsonAsync(
            $"repos/{owner}/{repo}/pulls/{pullNumber}",
            new { state = "closed" },
            JsonOptions,
            ct);

        await EnsureSuccessAsync(response, $"PR #{pullNumber} kapatilamadi", ct);
    }

    private HttpClient CreateAnonymousClient()
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    private async Task<HttpClient> CreateAppClientAsync(GitHubAppRegistration app, CancellationToken ct)
    {
        _ = ct;
        var client = httpClientFactory.CreateClient(HttpClientName);
        var jwt = GitHubAppJwt.Create(app.AppId, app.PrivateKeyPem);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }

    private async Task<HttpClient> CreateInstallationClientAsync(
        GitHubAppRegistration app,
        long installationId,
        CancellationToken ct)
    {
        var appClient = await CreateAppClientAsync(app, ct);
        using var response = await appClient.PostAsync(
            $"app/installations/{installationId}/access_tokens",
            content: null,
            ct);

        await EnsureSuccessAsync(response, "installation token alinamadi", ct);

        var tokenDto = await response.Content.ReadFromJsonAsync<InstallationTokenDto>(JsonOptions, ct);
        var token = tokenDto?.Token
                    ?? throw new InvalidOperationException("GitHub installation token bos dondu.");

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static GitHubRepoInfo ToRepoInfo(RepoDto repo)
    {
        var owner = repo.Owner?.Login ?? "";
        var name = repo.Name ?? "";
        var fullName = !string.IsNullOrWhiteSpace(repo.FullName)
            ? repo.FullName!
            : string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name)
                ? name
                : $"{owner}/{name}";

        return new GitHubRepoInfo(
            repo.Id,
            fullName,
            name,
            owner,
            repo.Private,
            string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "main" : repo.DefaultBranch!,
            repo.HtmlUrl ?? "",
            repo.Description);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var message = TryExtractMessage(body) ?? Truncate(body);
        throw new InvalidOperationException($"GitHub: {action} ({(int)response.StatusCode}): {message}");
    }

    private static string? TryExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    private static string Truncate(string value, int max = 300) =>
        value.Length <= max ? value : value[..max] + "…";

    /// <summary>Link header'daki rel="next" URL'sini API kokune gore relatif hale getirir.</summary>
    private static string? GetNextLink(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!part.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var start = part.IndexOf('<');
                var end = part.IndexOf('>');
                if (start < 0 || end <= start)
                {
                    continue;
                }

                var absolute = part[(start + 1)..end];
                const string prefix = "https://api.github.com/";
                return absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? absolute[prefix.Length..]
                    : absolute;
            }
        }

        return null;
    }

    private sealed class InstallationTokenDto
    {
        public string? Token { get; set; }
    }

    private sealed class InstallationDto
    {
        public long Id { get; set; }
        public AccountDto? Account { get; set; }
    }

    private sealed class AccountDto
    {
        public string? Login { get; set; }
        public string? Type { get; set; }

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }
    }

    private sealed class InstallationReposDto
    {
        public List<RepoDto> Repositories { get; set; } = [];
    }

    private sealed class RepoDto
    {
        public long Id { get; set; }

        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }

        public string? Name { get; set; }
        public bool Private { get; set; }

        [JsonPropertyName("default_branch")]
        public string? DefaultBranch { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        public string? Description { get; set; }
        public AccountDto? Owner { get; set; }
    }

    private sealed class BranchDto
    {
        public string? Name { get; set; }
        public bool Protected { get; set; }
        public CommitDto? Commit { get; set; }
    }

    private sealed class CommitDto
    {
        public string? Sha { get; set; }
    }

    private sealed class PullRequestDto
    {
        public int Number { get; set; }
        public string? Title { get; set; }
        public string? State { get; set; }
        public bool Draft { get; set; }
        public bool? Mergeable { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [JsonPropertyName("merged_at")]
        public DateTimeOffset? MergedAt { get; set; }

        [JsonPropertyName("closed_at")]
        public DateTimeOffset? ClosedAt { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        public AccountDto? User { get; set; }
        public RefDto? Head { get; set; }
        public RefDto? Base { get; set; }
    }

    private sealed class CommitHistoryDto
    {
        public string? Sha { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        public AccountDto? Author { get; set; }
        public CommitDetailDto? Commit { get; set; }
    }

    private sealed class CommitDetailDto
    {
        public string? Message { get; set; }
        public CommitPersonDto? Author { get; set; }
        public CommitPersonDto? Committer { get; set; }
    }

    private sealed class CommitPersonDto
    {
        public string? Name { get; set; }
        public DateTimeOffset? Date { get; set; }
    }

    private sealed class RefDto
    {
        public string? Ref { get; set; }
    }

    private sealed class GitRefDto
    {
        [JsonPropertyName("object")]
        public GitObjectDto? Object { get; set; }
    }

    private sealed class GitObjectDto
    {
        public string? Sha { get; set; }
    }
}
