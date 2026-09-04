using ClaudeReviewBot.Core.GitHub;

namespace ClaudeReviewBot.Tests;

public sealed class RepoPathTests
{
    [Theory]
    [InlineData("src/Orders/OrderService.cs")]
    [InlineData("README.md")]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData("dir with spaces/file name.txt")]
    public void Accepts_repository_relative_paths(string path)
    {
        Assert.True(RepoPath.IsSafe(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/etc/passwd")]
    [InlineData("../secrets.json")]
    [InlineData("src/../../x")]
    [InlineData("src/./x")]
    [InlineData("src//x")]
    [InlineData(@"src\x.cs")]
    [InlineData("a\0b")]
    public void Rejects_anything_that_could_escape_or_confuse(string? path)
    {
        Assert.False(RepoPath.IsSafe(path));
    }

    [Fact]
    public void Rejects_absurdly_long_paths()
    {
        Assert.False(RepoPath.IsSafe(new string('a', RepoPath.MaxLength + 1)));
    }
}
