using ClaudeReviewBot.Core;
using ClaudeReviewBot.Core.Diff;
using ClaudeReviewBot.Core.GitHub;
using ClaudeReviewBot.Core.Review;
using ClaudeReviewBot.Tests.Fakes;

namespace ClaudeReviewBot.Tests;

public sealed class ReviewOrchestratorTests
{
    private const string Patch = "@@ -1,2 +1,3 @@\n keep\n+added one\n+added two";

    private static readonly ChangedFile Code = new("src/A.cs", Patch);
    private static readonly ChangedFile Lockfile = new("package-lock.json", Patch);

    [Fact]
    public async Task Posts_one_review_with_only_commentable_findings()
    {
        FakePullRequestClient gitHub = new();
        gitHub.Files.AddRange([Code, Lockfile]);
        FixtureReviewer reviewer = new(new ReviewFindings(
        [
            new Finding("src/A.cs", 2, Severity.Blocker, "bad"),
            new Finding("src/A.cs", 1, Severity.Nit, "context line, must be dropped"),
            new Finding("package-lock.json", 2, Severity.Nit, "never reviewed"),
        ]));
        RecordingLog log = new();
        ReviewOrchestrator orchestrator = new(reviewer, new GeneratedFileFilter(), log);

        ReviewOutcome outcome = await orchestrator.ReviewPullRequestAsync(gitHub, "acme/orders", 42, new ReviewOptions(), CancellationToken.None);

        Assert.Equal(ReviewStatus.Posted, outcome.Status);
        ReviewSubmission posted = Assert.Single(gitHub.PostedReviews);
        Assert.Equal("abc123def456", posted.CommitId);
        ReviewComment comment = Assert.Single(posted.Comments);
        Assert.Equal(("src/A.cs", 2), (comment.Path, comment.Line));
        Assert.StartsWith("**Blocker:**", comment.Body, StringComparison.Ordinal);
        Assert.Contains(ReviewRenderer.Marker("abc123def456"), posted.Body, StringComparison.Ordinal);
        Assert.Contains("dropped (outside the diff): src/A.cs:1", log.Infos);
        Assert.Contains("dropped (outside the diff): package-lock.json:2", log.Infos);
        Assert.Contains("skipped (generated file): package-lock.json", log.Infos);
    }

    [Fact]
    public async Task Dry_run_never_posts_but_still_prepares_the_review()
    {
        FakePullRequestClient gitHub = new();
        gitHub.Files.Add(Code);
        ReviewOrchestrator orchestrator = new(new FixtureReviewer(new ReviewFindings([new Finding("src/A.cs", 3, Severity.Consider, "hm")])), new GeneratedFileFilter(), NullReviewLog.Instance);

        ReviewOutcome outcome = await orchestrator.ReviewPullRequestAsync(gitHub, "acme/orders", 42, new ReviewOptions { DryRun = true }, CancellationToken.None);

        Assert.Equal(ReviewStatus.DryRun, outcome.Status);
        Assert.Empty(gitHub.PostedReviews);
        Assert.Single(outcome.Review!.Submission.Comments);
    }

    [Fact]
    public async Task Does_not_post_twice_for_the_same_head_commit()
    {
        FakePullRequestClient gitHub = new();
        gitHub.Files.Add(Code);
        ReviewOrchestrator orchestrator = new(FixtureReviewer.Empty, new GeneratedFileFilter(), NullReviewLog.Instance);

        ReviewOutcome first = await orchestrator.ReviewPullRequestAsync(gitHub, "acme/orders", 42, new ReviewOptions(), CancellationToken.None);
        ReviewOutcome second = await orchestrator.ReviewPullRequestAsync(gitHub, "acme/orders", 42, new ReviewOptions(), CancellationToken.None);

        Assert.Equal(ReviewStatus.Posted, first.Status);
        Assert.Equal(ReviewStatus.AlreadyReviewed, second.Status);
        Assert.Single(gitHub.PostedReviews);
    }

    [Fact]
    public async Task Posts_a_review_even_with_zero_findings_so_the_run_is_visible()
    {
        FakePullRequestClient gitHub = new();
        gitHub.Files.Add(Code);
        ReviewOrchestrator orchestrator = new(FixtureReviewer.Empty, new GeneratedFileFilter(), NullReviewLog.Instance);

        ReviewOutcome outcome = await orchestrator.ReviewPullRequestAsync(gitHub, "acme/orders", 42, new ReviewOptions(), CancellationToken.None);

        Assert.Equal(ReviewStatus.Posted, outcome.Status);
        ReviewSubmission posted = Assert.Single(gitHub.PostedReviews);
        Assert.Empty(posted.Comments);
        Assert.Contains("found nothing worth a comment", posted.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_to_review_when_every_file_is_generated_or_patchless()
    {
        FakePullRequestClient gitHub = new();
        gitHub.Files.AddRange([Lockfile, new ChangedFile("image.png", string.Empty)]);
        ReviewOrchestrator orchestrator = new(new ThrowingReviewer(), new GeneratedFileFilter(), NullReviewLog.Instance);

        ReviewOutcome outcome = await orchestrator.ReviewPullRequestAsync(gitHub, "acme/orders", 42, new ReviewOptions(), CancellationToken.None);

        Assert.Equal(ReviewStatus.NothingToReview, outcome.Status);
        Assert.Empty(gitHub.PostedReviews);
    }

    [Fact]
    public async Task Max_findings_caps_the_comments()
    {
        FakePullRequestClient gitHub = new();
        gitHub.Files.Add(Code);
        FixtureReviewer reviewer = new(new ReviewFindings(
        [
            new Finding("src/A.cs", 2, Severity.Nit, "a"),
            new Finding("src/A.cs", 3, Severity.Nit, "b"),
        ]));
        ReviewOrchestrator orchestrator = new(reviewer, new GeneratedFileFilter(), NullReviewLog.Instance);

        ReviewOutcome outcome = await orchestrator.ReviewPullRequestAsync(gitHub, "acme/orders", 42, new ReviewOptions { MaxFindings = 1 }, CancellationToken.None);

        Assert.Single(outcome.Review!.Submission.Comments);
        Assert.Single(outcome.Review.Filter.OverCap);
    }

    [Fact]
    public async Task Offline_diff_review_is_always_a_dry_run()
    {
        ReviewOrchestrator orchestrator = new(new FixtureReviewer(new ReviewFindings([new Finding("src/A.cs", 2, Severity.Nit, "x")])), new GeneratedFileFilter(), NullReviewLog.Instance);

        ReviewOutcome outcome = await orchestrator.ReviewDiffAsync([Code], new ReviewOptions(), CancellationToken.None);

        Assert.Equal(ReviewStatus.DryRun, outcome.Status);
        Assert.Single(outcome.Review!.Submission.Comments);
    }

    private sealed class ThrowingReviewer : IReviewer
    {
        public Task<ReviewFindings> ReviewAsync(ReviewRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The reviewer must not be called when there is nothing to review.");
    }
}
