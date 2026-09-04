using ClaudeReviewBot.Core.GitHub;
using ClaudeReviewBot.Core.Review;

namespace ClaudeReviewBot.Tests;

public sealed class ReviewRendererTests
{
    [Fact]
    public void Body_carries_the_marker_the_counts_and_the_comment_only_disclaimer()
    {
        FilterResult result = new(
            Kept:
            [
                new Finding("a.cs", 1, Severity.Blocker, "b"),
                new Finding("a.cs", 2, Severity.Consider, "c"),
                new Finding("a.cs", 3, Severity.Nit, "n"),
            ],
            OutsideDiff: [],
            Duplicates: [],
            OverCap: []);

        ReviewSubmission submission = ReviewRenderer.ToSubmission("deadbeef", 2, result, "claude-opus-5");

        Assert.StartsWith(ReviewRenderer.Marker("deadbeef"), submission.Body, StringComparison.Ordinal);
        Assert.Contains("reviewed 2 changed files and left 3 comments: 1 blocker, 1 to consider, 1 nit.", submission.Body, StringComparison.Ordinal);
        Assert.Contains("never approves or requests changes", submission.Body, StringComparison.Ordinal);
        Assert.Equal(["**Blocker:** b", "**Consider:** c", "**Nit:** n"], submission.Comments.Select(c => c.Body));
    }

    [Fact]
    public void Console_view_lists_everything_that_was_not_posted()
    {
        FilterResult result = new(
            Kept: [new Finding("a.cs", 1, Severity.Nit, "kept")],
            OutsideDiff: [new Finding("a.cs", 9, Severity.Nit, "out")],
            Duplicates: [new Finding("a.cs", 1, Severity.Nit, "dupe")],
            OverCap: [new Finding("b.cs", 2, Severity.Nit, "cap")]);
        ReviewSubmission submission = ReviewRenderer.ToSubmission("sha", 1, result, "m");

        string console = ReviewRenderer.ToConsole(submission, result, ["yarn.lock"]);

        Assert.Contains("a.cs:1  **Nit:** kept", console, StringComparison.Ordinal);
        Assert.Contains("skipped (generated file): yarn.lock", console, StringComparison.Ordinal);
        Assert.Contains("dropped (outside the diff): a.cs:9", console, StringComparison.Ordinal);
        Assert.Contains("dropped (duplicate line): a.cs:1", console, StringComparison.Ordinal);
        Assert.Contains("dropped (over max-findings): b.cs:2", console, StringComparison.Ordinal);
    }
}
