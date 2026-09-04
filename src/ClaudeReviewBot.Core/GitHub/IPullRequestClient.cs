using ClaudeReviewBot.Core.Diff;

namespace ClaudeReviewBot.Core.GitHub;

/// <summary>
/// The five GitHub calls the bot makes, and nothing else. There is no merge, no approve, no
/// label: the worst outcome of a successful prompt injection is a review that stays quiet.
/// </summary>
public interface IPullRequestClient
{
    Task<PullRequestInfo> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string repository, int number, CancellationToken cancellationToken);

    /// <summary>Reads a file at a commit through the API. Returns <c>null</c> when the path does not exist there.</summary>
    Task<string?> ReadFileAtCommitAsync(string repository, string commitSha, string path, CancellationToken cancellationToken);

    /// <summary>True when a review carrying <paramref name="bodyMarker"/> already exists for <paramref name="commitSha"/>.</summary>
    Task<bool> HasReviewForCommitAsync(string repository, int number, string commitSha, string bodyMarker, CancellationToken cancellationToken);

    Task PostReviewAsync(string repository, int number, ReviewSubmission review, CancellationToken cancellationToken);
}
