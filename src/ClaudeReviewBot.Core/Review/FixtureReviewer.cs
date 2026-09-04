namespace ClaudeReviewBot.Core.Review;

/// <summary>
/// Returns canned findings. This is what runs when <c>ANTHROPIC_API_KEY</c> is absent, so the
/// whole pipeline (parsing, the commentable-line check, filtering, rendering) can be
/// exercised in CI and on a laptop without spending a token or holding a secret.
/// </summary>
public sealed class FixtureReviewer(ReviewFindings findings) : IReviewer
{
    public static FixtureReviewer Empty { get; } = new(ReviewFindings.Empty);

    public static FixtureReviewer FromFile(string path) => new(FindingsJson.Parse(File.ReadAllText(path)));

    public Task<ReviewFindings> ReviewAsync(ReviewRequest request, CancellationToken cancellationToken) => Task.FromResult(findings);
}
