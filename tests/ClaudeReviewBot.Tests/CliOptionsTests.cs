namespace ClaudeReviewBot.Tests;

public sealed class CliOptionsTests
{
    private static string? NoEnv(string _) => null;

    [Fact]
    public void Defaults_are_sensible()
    {
        CliOptions options = CliOptions.Parse([], NoEnv);

        Assert.Null(options.Repository);
        Assert.False(options.DryRun);
        Assert.Equal("claude-opus-5", options.Model);
        Assert.Equal(15, options.MaxFindings);
        Assert.False(options.Offline);
    }

    [Fact]
    public void Environment_variables_are_how_the_action_configures_it()
    {
        Dictionary<string, string> env = new()
        {
            ["GITHUB_REPOSITORY"] = "acme/orders",
            ["PR_NUMBER"] = "42",
            ["DRY_RUN"] = "true",
            ["CLAUDE_MODEL"] = "claude-sonnet-5",
            ["MAX_FINDINGS"] = "5",
            ["IGNORE_GLOBS"] = "docs/**, *.snap",
            ["PROJECT_CONTEXT"] = "Use records.",
        };

        CliOptions options = CliOptions.Parse([], k => env.GetValueOrDefault(k));

        Assert.Equal("acme/orders", options.Repository);
        Assert.Equal(42, options.PullRequest);
        Assert.True(options.DryRun);
        Assert.Equal("claude-sonnet-5", options.Model);
        Assert.Equal(5, options.MaxFindings);
        Assert.Equal(["docs/**", "*.snap"], options.IgnoreGlobs);
        Assert.Equal("Use records.", options.ProjectContext);
    }

    [Fact]
    public void Flags_override_the_environment()
    {
        CliOptions options = CliOptions.Parse(
            ["--repo", "other/repo", "--pr", "7", "--max-findings", "3", "--ignore", "*.md", "--dry-run"],
            k => k == "GITHUB_REPOSITORY" ? "acme/orders" : null);

        Assert.Equal("other/repo", options.Repository);
        Assert.Equal(7, options.PullRequest);
        Assert.Equal(3, options.MaxFindings);
        Assert.Equal(["*.md"], options.IgnoreGlobs);
        Assert.True(options.DryRun);
    }

    [Fact]
    public void Patch_switches_to_offline_mode()
    {
        CliOptions options = CliOptions.Parse(["--patch", "samples/sample.patch"], NoEnv);

        Assert.True(options.Offline);
        Assert.Equal("samples/sample.patch", options.PatchPath);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--pr")]
    [InlineData("--pr", "seven")]
    [InlineData("--max-findings", "-1")]
    public void Bad_input_is_a_usage_error(params string[] args)
    {
        Assert.Throws<CliUsageException>(() => CliOptions.Parse(args, NoEnv));
    }

    [Fact]
    public void Help_is_recognised()
    {
        Assert.True(CliOptions.Parse(["--help"], NoEnv).Help);
        Assert.True(CliOptions.Parse(["-h"], NoEnv).Help);
    }
}
