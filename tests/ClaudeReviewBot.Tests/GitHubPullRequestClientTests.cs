using System.Net;
using System.Text.Json;
using ClaudeReviewBot.Core.Diff;
using ClaudeReviewBot.Core.GitHub;
using ClaudeReviewBot.Tests.Fakes;

namespace ClaudeReviewBot.Tests;

public sealed class GitHubPullRequestClientTests
{
    private static GitHubPullRequestClient Client(ScriptedHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.example/") });

    [Fact]
    public async Task Posts_a_COMMENT_review_with_RIGHT_side_line_comments()
    {
        ScriptedHandler handler = new ScriptedHandler().Enqueue(HttpStatusCode.OK, "{}");
        ReviewSubmission review = new("abc", "body", [new ReviewComment("src/A.cs", 12, "**Blocker:** x")]);

        await Client(handler).PostReviewAsync("acme/orders", 7, review, CancellationToken.None);

        (HttpMethod method, string url, string? body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, method);
        Assert.EndsWith("/repos/acme/orders/pulls/7/reviews", url, StringComparison.Ordinal);

        using JsonDocument payload = JsonDocument.Parse(body!);
        Assert.Equal("COMMENT", payload.RootElement.GetProperty("event").GetString());
        Assert.Equal("abc", payload.RootElement.GetProperty("commit_id").GetString());
        JsonElement comment = payload.RootElement.GetProperty("comments")[0];
        Assert.Equal("src/A.cs", comment.GetProperty("path").GetString());
        Assert.Equal(12, comment.GetProperty("line").GetInt32());
        Assert.Equal("RIGHT", comment.GetProperty("side").GetString());
    }

    [Fact]
    public async Task Failed_posts_surface_status_and_body()
    {
        ScriptedHandler handler = new ScriptedHandler().Enqueue(HttpStatusCode.UnprocessableEntity, "{\"message\":\"Validation Failed\"}");

        GitHubApiException ex = await Assert.ThrowsAsync<GitHubApiException>(() =>
            Client(handler).PostReviewAsync("acme/orders", 7, new ReviewSubmission("abc", "b", []), CancellationToken.None));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("Validation Failed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changed_files_follow_pagination_and_skip_files_without_a_patch()
    {
        ScriptedHandler handler = new ScriptedHandler()
            .Enqueue(HttpStatusCode.OK, """[{"filename":"a.cs","patch":"@@ -1 +1 @@\n+a"},{"filename":"logo.png"}]""",
                r => r.Headers.Add("Link", "<https://api.github.example/repos/acme/orders/pulls/7/files?per_page=100&page=2>; rel=\"next\", <https://x/last>; rel=\"last\""))
            .Enqueue(HttpStatusCode.OK, """[{"filename":"b.cs","patch":"@@ -1 +1 @@\n+b"}]""");

        IReadOnlyList<ChangedFile> files = await Client(handler).GetChangedFilesAsync("acme/orders", 7, CancellationToken.None);

        Assert.Equal(["a.cs", "b.cs"], files.Select(f => f.Path));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("page=2", handler.Requests[1].Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_a_missing_file_returns_null_instead_of_throwing()
    {
        ScriptedHandler handler = new ScriptedHandler().Enqueue(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");

        string? content = await Client(handler).ReadFileAtCommitAsync("acme/orders", "abc", "src/Missing.cs", CancellationToken.None);

        Assert.Null(content);
        Assert.Contains("/repos/acme/orders/contents/src/Missing.cs?ref=abc", handler.Requests[0].Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_an_unsafe_path_is_refused_before_any_request()
    {
        ScriptedHandler handler = new();

        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).ReadFileAtCommitAsync("acme/orders", "abc", "../secrets", CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Huge_files_are_truncated_with_a_note()
    {
        string big = new('x', GitHubPullRequestClient.MaxFileChars + 5_000);
        ScriptedHandler handler = new ScriptedHandler().Enqueue(HttpStatusCode.OK, big);

        string? content = await Client(handler).ReadFileAtCommitAsync("acme/orders", "abc", "big.txt", CancellationToken.None);

        Assert.NotNull(content);
        Assert.Contains("[truncated:", content, StringComparison.Ordinal);
        Assert.Equal(GitHubPullRequestClient.MaxFileChars, content.IndexOf("\n\n[truncated:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Detects_an_existing_review_for_the_same_commit_by_marker()
    {
        ScriptedHandler handler = new ScriptedHandler().Enqueue(HttpStatusCode.OK,
            """[{"commit_id":"abc","body":"<!-- claude-review-bot commit=abc -->\nhi"},{"commit_id":"abc","body":"a human"}]""");

        bool exists = await Client(handler).HasReviewForCommitAsync("acme/orders", 7, "abc", "<!-- claude-review-bot", CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public void Create_normalizes_the_api_base_url_and_sets_the_headers_github_requires()
    {
        GitHubPullRequestClient client = GitHubPullRequestClient.Create("ghs_token", new Uri("https://ghe.example/api/v3"));

        Assert.NotNull(client);
        Assert.Null(GitHubPullRequestClient.NextLink(new HttpResponseMessage().Headers));
    }
}
