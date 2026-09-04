using Anthropic;
using Anthropic.Exceptions;
using ClaudeReviewBot;
using ClaudeReviewBot.Core;
using ClaudeReviewBot.Core.Diff;
using ClaudeReviewBot.Core.GitHub;
using ClaudeReviewBot.Core.Review;

const int ExitOk = 0;
const int ExitFailure = 1;
const int ExitUsage = 2;

bool onActions = string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
IReviewLog log = ConsoleReviewLog.ForEnvironment();

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

CliOptions options;
try
{
    options = CliOptions.Parse(args, Environment.GetEnvironmentVariable);
}
catch (CliUsageException ex)
{
    Fail(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.Usage);
    return ExitUsage;
}

if (options.Help)
{
    Console.WriteLine(CliOptions.Usage);
    return ExitOk;
}

string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
string? gitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

try
{
    GitHubPullRequestClient? gitHub = null;
    if (!options.Offline)
    {
        if (options.Repository is null || options.PullRequest is null)
        {
            Fail("Either --patch FILE (offline) or both --repo and --pr are required.");
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliOptions.Usage);
            return ExitUsage;
        }

        if (string.IsNullOrWhiteSpace(gitHubToken))
        {
            Fail("GITHUB_TOKEN is not set. Reviewing a pull request needs a token with pull-requests: write.");
            return ExitUsage;
        }

        Uri? apiUrl = Uri.TryCreate(Environment.GetEnvironmentVariable("GITHUB_API_URL"), UriKind.Absolute, out Uri? parsed) ? parsed : null;
        gitHub = GitHubPullRequestClient.Create(gitHubToken, apiUrl);
    }

    bool dryRun = options.DryRun;
    IReviewer reviewer;

    if (options.FixturePath is null && !string.IsNullOrWhiteSpace(apiKey))
    {
        HeadFileReader readFile = gitHub is null
            ? (_, _, _) => Task.FromResult<string?>("File contents are not available when reviewing a local patch; judge from the diff alone.")
            : (request, path, ct) => gitHub.ReadFileAtCommitAsync(request.Repository, request.HeadSha, path, ct);

        reviewer = new ClaudeReviewer(
            new AnthropicClient { ApiKey = apiKey },
            readFile,
            new ClaudeReviewerOptions { Model = options.Model, ProjectContext = options.ProjectContext },
            log);
    }
    else
    {
        string? fixture = options.FixturePath ?? FindDefaultFixture();
        if (options.FixturePath is null)
        {
            log.Warn(fixture is null
                ? "ANTHROPIC_API_KEY is not set and no fixture was found; the reviewer will return zero findings. Nothing will be posted."
                : $"ANTHROPIC_API_KEY is not set; using fixture findings from {fixture}. Nothing will be posted.");
        }
        else
        {
            log.Info($"Using fixture findings from {fixture}. Nothing will be posted.");
        }

        reviewer = fixture is null ? FixtureReviewer.Empty : FixtureReviewer.FromFile(fixture);
        dryRun = true;
    }

    ReviewOrchestrator orchestrator = new(reviewer, new GeneratedFileFilter(options.IgnoreGlobs), log);
    ReviewOptions reviewOptions = new() { MaxFindings = options.MaxFindings, DryRun = dryRun, Model = options.Model };

    ReviewOutcome outcome;
    if (options.Offline)
    {
        string diff = await File.ReadAllTextAsync(options.PatchPath!, cts.Token);
        IReadOnlyList<ChangedFile> files = PatchParser.ParseUnifiedDiff(diff);
        log.Info($"Parsed {files.Count} file(s) from {options.PatchPath}.");
        outcome = await orchestrator.ReviewDiffAsync(files, reviewOptions, cts.Token);
    }
    else
    {
        outcome = await orchestrator.ReviewPullRequestAsync(gitHub!, options.Repository!, options.PullRequest!.Value, reviewOptions, cts.Token);
    }

    if (outcome.Review is { } review && outcome.Status != ReviewStatus.Posted)
    {
        Console.WriteLine();
        Console.WriteLine(ReviewRenderer.ToConsole(review.Submission, review.Filter, review.SkippedGenerated));
    }

    log.Info($"Done: {outcome.Status}.");
    return ExitOk;
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Fail("Cancelled.");
    return ExitFailure;
}
catch (FileNotFoundException ex)
{
    Fail($"File not found: {ex.FileName ?? ex.Message}");
    return ExitUsage;
}
catch (AnthropicUnauthorizedException)
{
    Fail("Claude rejected the API key (401). Check ANTHROPIC_API_KEY.");
    return ExitFailure;
}
catch (AnthropicRateLimitException ex)
{
    Fail($"Claude rate limit exceeded after the SDK's retries: {ex.Message}");
    return ExitFailure;
}
catch (AnthropicBadRequestException ex)
{
    Fail($"Claude rejected the request (400). If you changed the schema, remember the structured-outputs dialect is a subset: {ex.Message}");
    return ExitFailure;
}
catch (AnthropicException ex)
{
    Fail($"Claude API error: {ex.Message}");
    return ExitFailure;
}
catch (GitHubApiException ex)
{
    Fail(ex.Message);
    return ExitFailure;
}
catch (InvalidOperationException ex)
{
    Fail(ex.Message);
    return ExitFailure;
}

void Fail(string message) => Console.Error.WriteLine(onActions ? $"::error::{message}" : $"error: {message}");

// Walk up from the executable looking for samples/findings.sample.json, so a bare
// `dotnet run` inside the repo and the composite action both find it without configuration.
static string? FindDefaultFixture()
{
    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    while (dir is not null)
    {
        string candidate = Path.Combine(dir.FullName, "samples", "findings.sample.json");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return null;
}
