using ClaudeReviewBot.Core.Diff;

namespace ClaudeReviewBot.Tests;

public sealed class GeneratedFileFilterTests
{
    private readonly GeneratedFileFilter _filter = new();

    [Theory]
    [InlineData("package-lock.json")]
    [InlineData("web/package-lock.json")]
    [InlineData("src/App/packages.lock.json")]
    [InlineData("src/App/Forms/Main.Designer.cs")]
    [InlineData("obj/Generated/Thing.g.cs")]
    [InlineData("wwwroot/js/site.min.js")]
    [InlineData("src/App/Migrations/20260101_Init.cs")]
    [InlineData("src/App/Data/Migrations/AppDbContextModelSnapshot.cs")]
    [InlineData(@"src\App\Migrations\Windows.cs")]
    public void Default_patterns_match_generated_files(string path)
    {
        Assert.True(_filter.IsGenerated(path));
    }

    [Theory]
    [InlineData("src/App/OrderService.cs")]
    [InlineData("src/App/Migrations.cs")]
    [InlineData("docs/lockfiles.md")]
    [InlineData("package.json")]
    [InlineData("src/App/MigrationsRunner/Runner.cs")]
    public void Default_patterns_leave_real_code_alone(string path)
    {
        Assert.False(_filter.IsGenerated(path));
    }

    [Fact]
    public void Extra_patterns_extend_the_defaults()
    {
        GeneratedFileFilter filter = new(["docs/**", "*.snapshot.txt"]);

        Assert.True(filter.IsGenerated("docs/api/index.md"));
        Assert.True(filter.IsGenerated("tests/golden/output.snapshot.txt"));
        Assert.True(filter.IsGenerated("yarn.lock"));
        Assert.False(filter.IsGenerated("src/docs.cs"));
    }

    [Fact]
    public void Blank_extra_patterns_are_ignored()
    {
        GeneratedFileFilter filter = new(["", "  ", "*.tmp"]);

        Assert.Equal(GeneratedFileFilter.DefaultPatterns.Count + 1, filter.Patterns.Count);
    }
}
