using System.Globalization;

namespace ClaudeReviewBot;

/// <summary>
/// Flags win over environment variables; environment variables are what the GitHub Action sets.
/// Parsing is by hand: a dozen options don't justify a dependency in a tool that builds from
/// source on every CI run.
/// </summary>
public sealed record CliOptions
{
    public string? Repository { get; init; }

    public int? PullRequest { get; init; }

    /// <summary>A unified diff on disk. Setting it switches to offline mode: no GitHub, no POST.</summary>
    public string? PatchPath { get; init; }

    /// <summary>Findings JSON used instead of Claude when there is no API key (or when explicitly requested).</summary>
    public string? FixturePath { get; init; }

    public bool DryRun { get; init; }

    public string Model { get; init; } = Core.Review.ClaudeReviewerOptions.DefaultModel;

    public int MaxFindings { get; init; } = 15;

    public IReadOnlyList<string> IgnoreGlobs { get; init; } = [];

    public string? ProjectContext { get; init; }

    public bool Help { get; init; }

    public bool Offline => PatchPath is not null;

    public static CliOptions Parse(IReadOnlyList<string> args, Func<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);

        CliOptions options = new()
        {
            Repository = Blank(env("GITHUB_REPOSITORY")),
            PullRequest = ParseInt(env("PR_NUMBER"), "PR_NUMBER"),
            FixturePath = Blank(env("FIXTURE_FINDINGS")),
            DryRun = IsTruthy(env("DRY_RUN")),
            Model = Blank(env("CLAUDE_MODEL")) ?? Core.Review.ClaudeReviewerOptions.DefaultModel,
            MaxFindings = ParseInt(env("MAX_FINDINGS"), "MAX_FINDINGS") ?? 15,
            IgnoreGlobs = SplitList(env("IGNORE_GLOBS")),
            ProjectContext = Blank(env("PROJECT_CONTEXT")),
        };

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string Next()
            {
                if (i + 1 >= args.Count)
                {
                    throw new CliUsageException($"{arg} requires a value.");
                }

                return args[++i];
            }

            options = arg switch
            {
                "--repo" => options with { Repository = Next() },
                "--pr" => options with { PullRequest = ParseInt(Next(), "--pr") },
                "--patch" => options with { PatchPath = Next() },
                "--fixture" => options with { FixturePath = Next() },
                "--dry-run" => options with { DryRun = true },
                "--model" => options with { Model = Next() },
                "--max-findings" => options with { MaxFindings = ParseInt(Next(), "--max-findings") ?? 15 },
                "--ignore" => options with { IgnoreGlobs = [.. options.IgnoreGlobs, .. SplitList(Next())] },
                "--project-context" => options with { ProjectContext = Next() },
                "-h" or "--help" => options with { Help = true },
                _ => throw new CliUsageException($"Unknown option '{arg}'. Try --help."),
            };
        }

        if (options.MaxFindings < 0)
        {
            throw new CliUsageException("--max-findings must be zero or positive.");
        }

        return options;
    }

    public const string Usage = """
        claude-review: a Claude-powered pull request reviewer that only ever comments.

        Usage:
          claude-review --repo OWNER/NAME --pr NUMBER [options]     review a pull request
          claude-review --patch FILE [options]                      review a unified diff on disk (offline, never posts)

        Options:
          --repo OWNER/NAME        Repository (env GITHUB_REPOSITORY)
          --pr NUMBER              Pull request number (env PR_NUMBER)
          --patch FILE             Unified diff to review instead of a pull request
          --fixture FILE           Findings JSON to use instead of calling Claude (env FIXTURE_FINDINGS)
          --dry-run                Do everything except post the review (env DRY_RUN=true)
          --model ID               Claude model, default claude-opus-5 (env CLAUDE_MODEL)
          --max-findings N         Cap on inline comments, default 15 (env MAX_FINDINGS)
          --ignore GLOBS           Extra file globs to skip, comma-separated (env IGNORE_GLOBS)
          --project-context TEXT   Repository conventions appended to the system prompt (env PROJECT_CONTEXT)
          -h, --help               This text

        Credentials (environment only, never flags):
          ANTHROPIC_API_KEY        Without it the bot uses fixture findings and forces --dry-run
          GITHUB_TOKEN             Needs pull-requests: write; the default Actions token is enough
          GITHUB_API_URL           Optional, for GitHub Enterprise Server
        """;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsTruthy(string? value) =>
        value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static int? ParseInt(string? value, string name)
    {
        if (Blank(value) is null)
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new CliUsageException($"{name} must be an integer, got '{value}'.");
    }

    private static IReadOnlyList<string> SplitList(string? value) =>
        Blank(value) is null
            ? []
            : [.. value!.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

public sealed class CliUsageException(string message) : Exception(message);
