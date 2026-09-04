using ClaudeReviewBot.Core.Diff;
using ClaudeReviewBot.Core.GitHub;

namespace ClaudeReviewBot.Core.Review;

public sealed record ReviewOptions
{
    public int MaxFindings { get; init; } = 15;

    public bool DryRun { get; init; }

    public string Model { get; init; } = ClaudeReviewerOptions.DefaultModel;
}

public enum ReviewStatus
{
    /// <summary>The review was posted to the pull request.</summary>
    Posted,

    /// <summary>Everything ran except the POST.</summary>
    DryRun,

    /// <summary>A review from this bot already exists for the head commit; nothing was re-posted.</summary>
    AlreadyReviewed,

    /// <summary>Every changed file was generated, binary, or too large for the API to include a patch.</summary>
    NothingToReview,
}

public sealed record PreparedReview(ReviewSubmission Submission, FilterResult Filter, IReadOnlyList<string> SkippedGenerated);

public sealed record ReviewOutcome(ReviewStatus Status, PreparedReview? Review);

/// <summary>
/// The pipeline: changed files, commentable lines, reviewer, filter, one review. The
/// reviewer never touches GitHub; this class is the only thing that decides to post.
/// </summary>
public sealed class ReviewOrchestrator(IReviewer reviewer, GeneratedFileFilter generatedFiles, IReviewLog log)
{
    public async Task<ReviewOutcome> ReviewPullRequestAsync(
        IPullRequestClient gitHub,
        string repository,
        int number,
        ReviewOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gitHub);
        ArgumentNullException.ThrowIfNull(options);

        PullRequestInfo pr = await gitHub.GetPullRequestAsync(repository, number, cancellationToken).ConfigureAwait(false);
        log.Info($"Reviewing {repository}#{number} at {pr.HeadSha[..Math.Min(7, pr.HeadSha.Length)]}: {pr.Title}");

        if (!options.DryRun && await gitHub.HasReviewForCommitAsync(repository, number, pr.HeadSha, ReviewRenderer.MarkerPrefix, cancellationToken).ConfigureAwait(false))
        {
            log.Info("A review for this commit already exists; nothing to do.");
            return new ReviewOutcome(ReviewStatus.AlreadyReviewed, null);
        }

        IReadOnlyList<ChangedFile> files = await gitHub.GetChangedFilesAsync(repository, number, cancellationToken).ConfigureAwait(false);
        PreparedReview? prepared = await PrepareAsync(repository, pr.HeadSha, files, options, cancellationToken).ConfigureAwait(false);

        if (prepared is null)
        {
            return new ReviewOutcome(ReviewStatus.NothingToReview, null);
        }

        if (options.DryRun)
        {
            log.Info("Dry run: not posting.");
            return new ReviewOutcome(ReviewStatus.DryRun, prepared);
        }

        await gitHub.PostReviewAsync(repository, number, prepared.Submission, cancellationToken).ConfigureAwait(false);
        log.Info($"Posted a review with {prepared.Submission.Comments.Count} inline comment(s).");
        return new ReviewOutcome(ReviewStatus.Posted, prepared);
    }

    /// <summary>Offline entry point: a diff from disk, no pull request, never a POST.</summary>
    public async Task<ReviewOutcome> ReviewDiffAsync(
        IReadOnlyList<ChangedFile> files,
        ReviewOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        PreparedReview? prepared = await PrepareAsync("local/diff", "0000000000000000000000000000000000000000", files, options, cancellationToken).ConfigureAwait(false);
        return prepared is null
            ? new ReviewOutcome(ReviewStatus.NothingToReview, null)
            : new ReviewOutcome(ReviewStatus.DryRun, prepared);
    }

    private async Task<PreparedReview?> PrepareAsync(
        string repository,
        string headSha,
        IReadOnlyList<ChangedFile> files,
        ReviewOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        List<string> skipped = [];
        List<ChangedFile> reviewable = [];

        foreach (ChangedFile file in files)
        {
            if (generatedFiles.IsGenerated(file.Path))
            {
                skipped.Add(file.Path);
                log.Info($"skipped (generated file): {file.Path}");
            }
            else if (!string.IsNullOrWhiteSpace(file.Patch))
            {
                reviewable.Add(file);
            }
        }

        if (reviewable.Count == 0)
        {
            log.Info("No reviewable files in this diff.");
            return null;
        }

        // Build the accepted set BEFORE asking Claude, then filter rather than hope.
        HashSet<(string Path, int Line)> commentable = PatchParser.CommentableLines(reviewable);
        log.Info($"{reviewable.Count} file(s) to review, {commentable.Count} commentable line(s).");

        ReviewFindings findings = await reviewer.ReviewAsync(new ReviewRequest(repository, headSha, reviewable), cancellationToken).ConfigureAwait(false);
        FilterResult filtered = FindingFilter.Apply(findings.Findings, commentable, options.MaxFindings);

        // Logging the drops matters: a bot that silently discards a third of its findings
        // looks identical to a bot that had nothing to say.
        foreach (Finding f in filtered.OutsideDiff)
        {
            log.Info($"dropped (outside the diff): {f.Path}:{f.Line}");
        }

        foreach (Finding f in filtered.Duplicates)
        {
            log.Info($"dropped (duplicate line): {f.Path}:{f.Line}");
        }

        foreach (Finding f in filtered.OverCap)
        {
            log.Info($"dropped (over max-findings={options.MaxFindings}): {f.Path}:{f.Line}");
        }

        log.Info($"{findings.Findings.Count} finding(s) from the reviewer, {filtered.Kept.Count} kept.");

        ReviewSubmission submission = ReviewRenderer.ToSubmission(headSha, reviewable.Count, filtered, options.Model);
        return new PreparedReview(submission, filtered, skipped);
    }
}
