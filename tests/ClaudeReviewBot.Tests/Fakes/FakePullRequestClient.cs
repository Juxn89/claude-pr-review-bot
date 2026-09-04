using ClaudeReviewBot.Core.Diff;
using ClaudeReviewBot.Core.GitHub;

namespace ClaudeReviewBot.Tests.Fakes;

internal sealed class FakePullRequestClient : IPullRequestClient
{
    public PullRequestInfo PullRequest { get; set; } = new(42, "Add order cache", "abc123def456", "000111222333");

    public List<ChangedFile> Files { get; } = [];

    public Dictionary<string, string> HeadFiles { get; } = [];

    public List<ReviewSubmission> PostedReviews { get; } = [];

    public HashSet<string> ReviewedCommits { get; } = [];

    public List<string> ReadPaths { get; } = [];

    public Task<PullRequestInfo> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken) =>
        Task.FromResult(PullRequest);

    public Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repository, int number, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ChangedFile>>(Files);

    public Task<string?> ReadFileAtCommitAsync(string repository, string commitSha, string path, CancellationToken cancellationToken)
    {
        ReadPaths.Add(path);
        return Task.FromResult(HeadFiles.TryGetValue(path, out string? content) ? content : null);
    }

    public Task<bool> HasReviewForCommitAsync(string repository, int number, string commitSha, string bodyMarker, CancellationToken cancellationToken) =>
        Task.FromResult(ReviewedCommits.Contains(commitSha) || PostedReviews.Any(r => r.CommitId == commitSha && r.Body.Contains(bodyMarker, StringComparison.Ordinal)));

    public Task PostReviewAsync(string repository, int number, ReviewSubmission review, CancellationToken cancellationToken)
    {
        PostedReviews.Add(review);
        return Task.CompletedTask;
    }
}
