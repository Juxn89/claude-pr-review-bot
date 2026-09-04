using ClaudeReviewBot.Core.Diff;
using ClaudeReviewBot.Core.Review;
using ClaudeReviewBot.Tests.Fakes;

namespace ClaudeReviewBot.Tests;

/// <summary>Runs the exact files the README points readers at, so the offline demo can't rot silently.</summary>
public sealed class SamplesEndToEndTests
{
    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "samples", name);

    [Fact]
    public async Task The_shipped_sample_produces_the_documented_review()
    {
        IReadOnlyList<ChangedFile> files = PatchParser.ParseUnifiedDiff(await File.ReadAllTextAsync(Sample("sample.patch")));
        RecordingLog log = new();
        ReviewOrchestrator orchestrator = new(FixtureReviewer.FromFile(Sample("findings.sample.json")), new GeneratedFileFilter(), log);

        ReviewOutcome outcome = await orchestrator.ReviewDiffAsync(files, new ReviewOptions(), CancellationToken.None);

        Assert.Equal(ReviewStatus.DryRun, outcome.Status);
        PreparedReview review = outcome.Review!;

        Assert.Equal(["src/Aurora.Orders.Api/packages.lock.json"], review.SkippedGenerated);
        Assert.Equal(5, review.Submission.Comments.Count);
        Assert.Equal(3, review.Submission.Comments.Count(c => c.Body.StartsWith("**Blocker:**", StringComparison.Ordinal)));
        Assert.Equal(2, review.Filter.OutsideDiff.Count);
        Assert.Single(review.Filter.Duplicates);
        Assert.Contains("dropped (outside the diff): src/Aurora.Orders.Api/Orders/OrderService.cs:13", log.Infos);
        Assert.Contains("3 changed files and left 5 comments: 3 blockers, 1 to consider, 1 nit.", review.Submission.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sample_patch_line_numbers_are_internally_consistent()
    {
        IReadOnlyList<ChangedFile> files = PatchParser.ParseUnifiedDiff(await File.ReadAllTextAsync(Sample("sample.patch")));
        HashSet<(string Path, int Line)> commentable = PatchParser.CommentableLines(files);

        // Every finding the fixture expects to be KEPT must land on an added line.
        Assert.Contains(("src/Aurora.Orders.Api/Orders/OrderService.cs", 15), commentable);
        Assert.Contains(("src/Aurora.Orders.Api/Payments/PaymentsClient.cs", 49), commentable);
        Assert.Contains(("src/Aurora.Orders.Api/Program.cs", 24), commentable);
        Assert.Contains(("src/Aurora.Orders.Api/Program.cs", 25), commentable);
        Assert.Contains(("src/Aurora.Orders.Api/Program.cs", 26), commentable);
        Assert.DoesNotContain(("src/Aurora.Orders.Api/Orders/OrderService.cs", 13), commentable);
    }
}
