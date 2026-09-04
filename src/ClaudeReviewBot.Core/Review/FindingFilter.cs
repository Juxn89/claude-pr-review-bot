namespace ClaudeReviewBot.Core.Review;

/// <summary>What survived, and why the rest did not. Every drop is reported, never silent.</summary>
public sealed record FilterResult(
    IReadOnlyList<Finding> Kept,
    IReadOnlyList<Finding> OutsideDiff,
    IReadOnlyList<Finding> Duplicates,
    IReadOnlyList<Finding> OverCap)
{
    public int DroppedCount => OutsideDiff.Count + Duplicates.Count + OverCap.Count;
}

/// <summary>
/// The part of the design that a tool-call-per-comment bot never gets to run: the whole set of
/// findings is in hand before anything touches GitHub, so it can be sorted, deduplicated,
/// capped, and checked against the lines GitHub will actually accept.
/// </summary>
public static class FindingFilter
{
    public static FilterResult Apply(
        IReadOnlyList<Finding> findings,
        IReadOnlySet<(string Path, int Line)> commentable,
        int maxFindings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(commentable);
        ArgumentOutOfRangeException.ThrowIfNegative(maxFindings);

        List<Finding> kept = [];
        List<Finding> outside = [];
        List<Finding> duplicates = [];
        List<Finding> overCap = [];
        HashSet<(string, int)> seen = [];

        IOrderedEnumerable<Finding> ordered = findings
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .ThenBy(f => f.Line);

        foreach (Finding finding in ordered)
        {
            if (!commentable.Contains((finding.Path, finding.Line)))
            {
                outside.Add(finding);
            }
            else if (!seen.Add((finding.Path, finding.Line)))
            {
                duplicates.Add(finding);
            }
            else if (kept.Count >= maxFindings)
            {
                overCap.Add(finding);
            }
            else
            {
                kept.Add(finding);
            }
        }

        return new FilterResult(kept, outside, duplicates, overCap);
    }
}
