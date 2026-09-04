using ClaudeReviewBot.Core.Review;

namespace ClaudeReviewBot.Tests;

public sealed class FindingFilterTests
{
    private static readonly HashSet<(string Path, int Line)> Commentable =
    [
        ("a.cs", 10), ("a.cs", 11), ("a.cs", 12), ("b.cs", 5),
    ];

    [Fact]
    public void Drops_findings_outside_the_diff_and_reports_them()
    {
        Finding inside = new("a.cs", 10, Severity.Nit, "ok");
        Finding outside = new("a.cs", 99, Severity.Blocker, "not in diff");

        FilterResult result = FindingFilter.Apply([inside, outside], Commentable, 10);

        Assert.Equal([inside], result.Kept);
        Assert.Equal([outside], result.OutsideDiff);
        Assert.Equal(1, result.DroppedCount);
    }

    [Fact]
    public void Orders_blockers_first_then_by_path_and_line()
    {
        Finding nit = new("a.cs", 10, Severity.Nit, "n");
        Finding consider = new("b.cs", 5, Severity.Consider, "c");
        Finding blocker = new("a.cs", 12, Severity.Blocker, "b");

        FilterResult result = FindingFilter.Apply([nit, consider, blocker], Commentable, 10);

        Assert.Equal([blocker, consider, nit], result.Kept);
    }

    [Fact]
    public void Keeps_the_most_severe_finding_per_line_and_drops_the_rest_as_duplicates()
    {
        Finding nit = new("a.cs", 10, Severity.Nit, "later");
        Finding blocker = new("a.cs", 10, Severity.Blocker, "first");

        FilterResult result = FindingFilter.Apply([nit, blocker], Commentable, 10);

        Assert.Equal([blocker], result.Kept);
        Assert.Equal([nit], result.Duplicates);
    }

    [Fact]
    public void Caps_the_total_and_reports_the_overflow()
    {
        Finding[] findings =
        [
            new("a.cs", 10, Severity.Blocker, "1"),
            new("a.cs", 11, Severity.Consider, "2"),
            new("a.cs", 12, Severity.Nit, "3"),
        ];

        FilterResult result = FindingFilter.Apply(findings, Commentable, 2);

        Assert.Equal(2, result.Kept.Count);
        Assert.Equal([findings[2]], result.OverCap);
    }

    [Fact]
    public void Zero_cap_keeps_nothing_but_still_classifies()
    {
        FilterResult result = FindingFilter.Apply([new("a.cs", 10, Severity.Nit, "x")], Commentable, 0);

        Assert.Empty(result.Kept);
        Assert.Single(result.OverCap);
    }

    [Fact]
    public void Negative_cap_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FindingFilter.Apply([], Commentable, -1));
    }
}
