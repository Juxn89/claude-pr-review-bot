using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeReviewBot.Core.Diff;

namespace ClaudeReviewBot.Core.GitHub;

/// <summary>
/// Talks to the GitHub REST API with a plain <see cref="HttpClient"/>. Note what is absent: a
/// checkout. The diff and every file the model asks for come from the API at the head SHA, so
/// contributor code is only ever read as data inside the job that holds the API key.
/// </summary>
public sealed partial class GitHubPullRequestClient(HttpClient http) : IPullRequestClient
{
    /// <summary>Files larger than this are truncated before reaching the model; 200 KB is already far past useful.</summary>
    public const int MaxFileChars = 200_000;

    public static readonly Uri DefaultApiBaseUrl = new("https://api.github.com/");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("<([^>]+)>;\\s*rel=\"next\"")]
    private static partial Regex NextLinkPattern { get; }

    public static GitHubPullRequestClient Create(string token, Uri? apiBaseUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        Uri baseUrl = apiBaseUrl ?? DefaultApiBaseUrl;
        if (!baseUrl.AbsoluteUri.EndsWith('/'))
        {
            baseUrl = new Uri(baseUrl.AbsoluteUri + "/");
        }

        HttpClient http = new(new RetryHandler(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }))
        {
            BaseAddress = baseUrl,
            Timeout = TimeSpan.FromSeconds(100),
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.UserAgent.ParseAdd("claude-pr-review-bot/1.0 (+https://github.com/Juxn89/claude-pr-review-bot)");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        return new GitHubPullRequestClient(http);
    }

    public async Task<PullRequestInfo> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken)
    {
        using JsonDocument doc = await GetJsonAsync($"repos/{repository}/pulls/{number}", cancellationToken).ConfigureAwait(false);
        JsonElement root = doc.RootElement;

        return new PullRequestInfo(
            root.GetProperty("number").GetInt32(),
            root.GetProperty("title").GetString() ?? string.Empty,
            root.GetProperty("head").GetProperty("sha").GetString()!,
            root.GetProperty("base").GetProperty("sha").GetString()!);
    }

    public async Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repository, int number, CancellationToken cancellationToken)
    {
        List<ChangedFile> files = [];

        await foreach (JsonElement file in PagedAsync($"repos/{repository}/pulls/{number}/files?per_page=100", cancellationToken).ConfigureAwait(false))
        {
            string path = file.GetProperty("filename").GetString()!;

            // No "patch" means binary, or a diff too large for the API to include. Nothing to comment on either way.
            if (file.TryGetProperty("patch", out JsonElement patch) && patch.ValueKind == JsonValueKind.String)
            {
                files.Add(new ChangedFile(path, patch.GetString()!));
            }
        }

        return files;
    }

    public async Task<string?> ReadFileAtCommitAsync(string repository, string commitSha, string path, CancellationToken cancellationToken)
    {
        if (!RepoPath.IsSafe(path))
        {
            throw new ArgumentException($"Refusing to read unsafe path '{path}'.", nameof(path));
        }

        string escapedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        using HttpRequestMessage request = new(HttpMethod.Get, $"repos/{repository}/contents/{escapedPath}?ref={Uri.EscapeDataString(commitSha)}");
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return content.Length <= MaxFileChars
            ? content
            : string.Concat(content.AsSpan(0, MaxFileChars), $"\n\n[truncated: file is {content.Length:N0} characters, showing the first {MaxFileChars:N0}]");
    }

    public async Task<bool> HasReviewForCommitAsync(string repository, int number, string commitSha, string bodyMarker, CancellationToken cancellationToken)
    {
        await foreach (JsonElement review in PagedAsync($"repos/{repository}/pulls/{number}/reviews?per_page=100", cancellationToken).ConfigureAwait(false))
        {
            string? reviewSha = review.TryGetProperty("commit_id", out JsonElement sha) ? sha.GetString() : null;
            string? body = review.TryGetProperty("body", out JsonElement b) ? b.GetString() : null;

            if (string.Equals(reviewSha, commitSha, StringComparison.OrdinalIgnoreCase)
                && body is not null
                && body.Contains(bodyMarker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public async Task PostReviewAsync(string repository, int number, ReviewSubmission review, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);

        // Always COMMENT. A bot that can only comment cannot be tricked into approving, and
        // cannot block a release by hallucinating a blocker.
        var payload = new
        {
            commit_id = review.CommitId,
            body = review.Body,
            @event = "COMMENT",
            comments = review.Comments.Select(c => new { path = c.Path, line = c.Line, side = "RIGHT", body = c.Body }).ToArray(),
        };

        using HttpResponseMessage response = await http
            .PostAsJsonAsync($"repos/{repository}/pulls/{number}/reviews", payload, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    private async IAsyncEnumerable<JsonElement> PagedAsync(string firstUrl, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? next = firstUrl;

        while (next is not null)
        {
            using HttpResponseMessage response = await http.GetAsync(next, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                yield return item.Clone();
            }

            next = NextLink(response.Headers);
        }
    }

    internal static string? NextLink(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Link", out IEnumerable<string>? values))
        {
            return null;
        }

        Match match = NextLinkPattern.Match(string.Join(",", values));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new GitHubApiException(
            response.StatusCode,
            response.RequestMessage?.Method.Method ?? "?",
            response.RequestMessage?.RequestUri?.ToString() ?? "?",
            body);
    }
}
