using ClaudeReviewBot.Core.Diff;

namespace ClaudeReviewBot.Core.Review;

public sealed record ReviewRequest(string Repository, string HeadSha, IReadOnlyList<ChangedFile> Files);

/// <summary>Turns a diff into findings. Claude in production; a fixture when there is no API key.</summary>
public interface IReviewer
{
    Task<ReviewFindings> ReviewAsync(ReviewRequest request, CancellationToken cancellationToken);
}
