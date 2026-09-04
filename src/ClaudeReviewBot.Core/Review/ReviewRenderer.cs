using System.Text;
using ClaudeReviewBot.Core.GitHub;

namespace ClaudeReviewBot.Core.Review;

/// <summary>Turns filtered findings into the exact review GitHub will receive, and into a console view of the same.</summary>
public static class ReviewRenderer
{
    /// <summary>Hidden marker in every review body; used to recognise our own reviews and skip re-posting for the same commit.</summary>
    public const string MarkerPrefix = "<!-- claude-review-bot";

    public static string Marker(string commitSha) => $"{MarkerPrefix} commit={commitSha} -->";

    public static ReviewSubmission ToSubmission(string headSha, int reviewedFileCount, FilterResult result, string model)
    {
        ArgumentNullException.ThrowIfNull(result);

        int blockers = result.Kept.Count(f => f.Severity == Severity.Blocker);
        int consider = result.Kept.Count(f => f.Severity == Severity.Consider);
        int nits = result.Kept.Count(f => f.Severity == Severity.Nit);

        string summary = result.Kept.Count == 0
            ? $"Claude reviewed {Plural(reviewedFileCount, "changed file")} and found nothing worth a comment."
            : $"Claude reviewed {Plural(reviewedFileCount, "changed file")} and left {Plural(result.Kept.Count, "comment")}: " +
              $"{Plural(blockers, "blocker")}, {consider} to consider, {Plural(nits, "nit")}.";

        string body = $"{Marker(headSha)}\n{summary}\n\n<sub>Model: {model}. This bot only comments; it never approves or requests changes.</sub>";

        List<ReviewComment> comments = [.. result.Kept.Select(f => new ReviewComment(f.Path, f.Line, $"**{Label(f.Severity)}** {f.Comment}"))];

        return new ReviewSubmission(headSha, body, comments);
    }

    public static string ToConsole(ReviewSubmission submission, FilterResult result, IReadOnlyList<string> skippedGenerated)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(skippedGenerated);

        StringBuilder sb = new();
        sb.Append("-- review body ---------------------------------------------\n");
        sb.Append(submission.Body).Append('\n');
        sb.Append("-- inline comments -----------------------------------------\n");

        if (submission.Comments.Count == 0)
        {
            sb.Append("(none)\n");
        }

        foreach (ReviewComment comment in submission.Comments)
        {
            sb.Append(comment.Path).Append(':').Append(comment.Line).Append("  ").Append(comment.Body).Append('\n');
        }

        if (skippedGenerated.Count > 0 || result.DroppedCount > 0)
        {
            sb.Append("-- not posted ----------------------------------------------\n");
        }

        foreach (string path in skippedGenerated)
        {
            sb.Append("skipped (generated file): ").Append(path).Append('\n');
        }

        foreach (Finding f in result.OutsideDiff)
        {
            sb.Append("dropped (outside the diff): ").Append(f.Path).Append(':').Append(f.Line).Append('\n');
        }

        foreach (Finding f in result.Duplicates)
        {
            sb.Append("dropped (duplicate line): ").Append(f.Path).Append(':').Append(f.Line).Append('\n');
        }

        foreach (Finding f in result.OverCap)
        {
            sb.Append("dropped (over max-findings): ").Append(f.Path).Append(':').Append(f.Line).Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    private static string Label(Severity severity) => severity switch
    {
        Severity.Blocker => "Blocker:",
        Severity.Consider => "Consider:",
        Severity.Nit => "Nit:",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null),
    };

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
