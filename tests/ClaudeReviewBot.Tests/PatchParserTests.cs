using ClaudeReviewBot.Core.Diff;

namespace ClaudeReviewBot.Tests;

public sealed class PatchParserTests
{
    private const string TwoHunks = """
        @@ -12,6 +12,7 @@ public sealed class OrderService
         {
             private readonly IOrderRepository _repository = repository;
             private readonly ILogger<OrderService> _logger = logger;
        +    private readonly Dictionary<string, Order> _cache = new();

             public async Task<Order?> GetAsync(string orderId, CancellationToken ct)
             {
        @@ -18,2 +19,4 @@ public sealed class OrderService
        -        return await _repository.FindAsync(orderId, ct);
        +        if (_cache.TryGetValue(orderId, out Order? cached))
        +            return cached;
        +        return await _repository.FindAsync(orderId, ct);
             }
        """;

    [Fact]
    public void AddedLines_reports_new_file_numbers_across_hunks()
    {
        int[] added = [.. PatchParser.AddedLines(TwoHunks)];

        Assert.Equal([15, 19, 20, 21], added);
    }

    [Fact]
    public void AddedLines_removed_lines_do_not_advance_the_counter()
    {
        const string patch = """
            @@ -1,3 +1,3 @@
             keep
            -old
            +new
             keep
            """;

        Assert.Equal([2], PatchParser.AddedLines(patch));
    }

    [Fact]
    public void AddedLines_handles_new_file_and_header_without_count()
    {
        const string patch = """
            @@ -0,0 +1 @@
            +only line
            """;

        Assert.Equal([1], PatchParser.AddedLines(patch));
    }

    [Fact]
    public void AddedLines_ignores_no_newline_marker_and_crlf()
    {
        string patch = "@@ -1,2 +1,2 @@\r\n line\r\n-old\r\n+new\r\n\\ No newline at end of file\r\n";

        Assert.Equal([2], PatchParser.AddedLines(patch));
    }

    [Fact]
    public void AddedLines_of_empty_patch_is_empty()
    {
        Assert.Empty(PatchParser.AddedLines(string.Empty));
    }

    [Fact]
    public void CommentableLines_is_keyed_by_path_and_line()
    {
        HashSet<(string Path, int Line)> set = PatchParser.CommentableLines(
        [
            new ChangedFile("a.cs", "@@ -1,1 +1,2 @@\n keep\n+added"),
            new ChangedFile("b.cs", "@@ -1,1 +1,2 @@\n keep\n+added"),
        ]);

        Assert.Contains(("a.cs", 2), set);
        Assert.Contains(("b.cs", 2), set);
        Assert.DoesNotContain(("a.cs", 1), set);
    }

    [Fact]
    public void ParseUnifiedDiff_splits_per_file_and_strips_the_b_prefix()
    {
        const string diff = """
            diff --git a/src/A.cs b/src/A.cs
            index 111..222 100644
            --- a/src/A.cs
            +++ b/src/A.cs
            @@ -1,1 +1,2 @@
             one
            +two
            diff --git a/README.md b/README.md
            --- a/README.md
            +++ b/README.md
            @@ -1,1 +1,1 @@
            -old
            +new
            """;

        IReadOnlyList<ChangedFile> files = PatchParser.ParseUnifiedDiff(diff);

        Assert.Equal(["src/A.cs", "README.md"], files.Select(f => f.Path));
        Assert.StartsWith("@@ -1,1 +1,2 @@", files[0].Patch, StringComparison.Ordinal);
        Assert.Equal([2], PatchParser.AddedLines(files[0].Patch));
        Assert.Equal([1], PatchParser.AddedLines(files[1].Patch));
    }

    [Fact]
    public void ParseUnifiedDiff_skips_deleted_files()
    {
        const string diff = """
            diff --git a/gone.cs b/gone.cs
            deleted file mode 100644
            --- a/gone.cs
            +++ /dev/null
            @@ -1,2 +0,0 @@
            -a
            -b
            diff --git a/new.cs b/new.cs
            new file mode 100644
            --- /dev/null
            +++ b/new.cs
            @@ -0,0 +1 @@
            +hello
            """;

        IReadOnlyList<ChangedFile> files = PatchParser.ParseUnifiedDiff(diff);

        Assert.Single(files);
        Assert.Equal("new.cs", files[0].Path);
    }

    [Fact]
    public void ParseUnifiedDiff_does_not_mistake_an_added_line_starting_with_plus_plus_for_a_header()
    {
        const string diff = """
            diff --git a/a.cs b/a.cs
            --- a/a.cs
            +++ b/a.cs
            @@ -1,1 +1,2 @@
             x
            ++++ this is content, not a header
            """;

        IReadOnlyList<ChangedFile> files = PatchParser.ParseUnifiedDiff(diff);

        Assert.Equal("a.cs", Assert.Single(files).Path);
        Assert.Equal([2], PatchParser.AddedLines(files[0].Patch));
    }
}
