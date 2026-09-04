using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using ClaudeReviewBot.Core.GitHub;
using ClaudeReviewBot.Core.Prompts;

namespace ClaudeReviewBot.Core.Review;

/// <summary>Reads a repository-relative file at the pull request's head commit; <c>null</c> when it does not exist.</summary>
public delegate Task<string?> HeadFileReader(ReviewRequest request, string path, CancellationToken cancellationToken);

public sealed record ClaudeReviewerOptions
{
    public const string DefaultModel = "claude-opus-5";

    public string Model { get; init; } = DefaultModel;

    /// <summary>Findings are compact JSON; 8k is generous. Hitting it means the model went off the rails, not that we need more.</summary>
    public int MaxTokens { get; init; } = 8_000;

    /// <summary>Upper bound on read_file round trips. Each one resends the whole conversation.</summary>
    public int MaxToolTurns { get; init; } = 8;

    public string? ProjectContext { get; init; }
}

/// <summary>
/// The reviewer from the article: structured outputs for the answer, exactly one tool for
/// context the diff cannot show. Both go in the same request; Claude may spend a few turns
/// reading files, and when it stops asking, the text it returns conforms to the schema.
/// </summary>
public sealed class ClaudeReviewer(AnthropicClient client, HeadFileReader readFile, ClaudeReviewerOptions options, IReviewLog log) : IReviewer
{
    public const string ReadFileToolName = "read_file";

    // Built once and never varied per PR: the compiled schema is cached server-side, and
    // changing the tool set invalidates that cache.
    private static readonly Tool ReadFileTool = new()
    {
        Name = ReadFileToolName,
        Description = "Read a file from the pull request's head commit. Use it when the diff alone does not show enough context to judge a change.",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["path"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Repository-relative path, e.g. src/Orders/OrderCache.cs" }),
            },
            Required = ["path"],
        },
    };

    private static readonly Dictionary<string, JsonElement> Schema = FindingsSchema.Create();

    public async Task<ReviewFindings> ReviewAsync(ReviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<TextBlockParam> system =
        [
            new() { Text = SystemPrompt.Build(options.ProjectContext), CacheControl = new CacheControlEphemeral() },
        ];

        List<MessageParam> messages = [new() { Role = Role.User, Content = DiffRenderer.Render(request.Files) }];
        string? findingsJson = null;

        for (int turn = 0; turn <= options.MaxToolTurns; turn++)
        {
            Message response = await client.Messages.Create(
                new MessageCreateParams
                {
                    Model = options.Model,
                    MaxTokens = options.MaxTokens,
                    System = system,
                    Tools = [ReadFileTool],
                    OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = Schema } },
                    Messages = messages,
                },
                cancellationToken).ConfigureAwait(false);

            LogUsage(turn, response);

            if (response.StopReason == "refusal")
            {
                log.Warn("Claude declined to review this diff (stop_reason=refusal). Posting no findings.");
                return ReviewFindings.Empty;
            }

            if (response.StopReason == "max_tokens")
            {
                throw new InvalidOperationException(
                    $"Claude hit the {options.MaxTokens}-token output limit before finishing the findings document. " +
                    "This usually means the diff is far too large; split the pull request or tighten the ignore patterns.");
            }

            List<ContentBlockParam> assistant = [];
            List<ContentBlockParam> toolResults = [];

            foreach (ContentBlock block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text))
                {
                    assistant.Add(new TextBlockParam { Text = text.Text });
                    findingsJson = text.Text;
                }
                else if (block.TryPickThinking(out ThinkingBlock? thinking))
                {
                    // Adaptive thinking is on by default; its blocks must round-trip untouched, signature included.
                    assistant.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
                {
                    assistant.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out ToolUseBlock? call))
                {
                    assistant.Add(new ToolUseBlockParam { ID = call.ID, Name = call.Name, Input = call.Input });
                    toolResults.Add(new ToolResultBlockParam
                    {
                        ToolUseID = call.ID,
                        Content = await HandleToolCallAsync(request, call, cancellationToken).ConfigureAwait(false),
                    });
                }
            }

            if (toolResults.Count == 0)
            {
                break;
            }

            messages.Add(new() { Role = Role.Assistant, Content = assistant });
            messages.Add(new() { Role = Role.User, Content = toolResults });
        }

        if (findingsJson is null)
        {
            throw new InvalidOperationException(
                $"Claude kept calling {ReadFileToolName} for {options.MaxToolTurns + 1} turns without returning findings.");
        }

        return FindingsJson.Parse(findingsJson);
    }

    private async Task<string> HandleToolCallAsync(ReviewRequest request, ToolUseBlock call, CancellationToken cancellationToken)
    {
        if (call.Name != ReadFileToolName)
        {
            return $"Unknown tool '{call.Name}'. The only available tool is {ReadFileToolName}.";
        }

        string? path = call.Input.TryGetValue("path", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

        if (!RepoPath.IsSafe(path))
        {
            log.Warn($"{ReadFileToolName} refused unsafe path: '{path}'");
            return "Refused: path must be repository-relative, with forward slashes and no '.' or '..' segments.";
        }

        log.Info($"{ReadFileToolName}: {path}");
        string? content = await readFile(request, path!, cancellationToken).ConfigureAwait(false);
        return content ?? $"No file at '{path}' in the pull request's head commit.";
    }

    private void LogUsage(int turn, Message response)
    {
        Usage usage = response.Usage;
        log.Info(
            $"claude turn {turn + 1}: {usage.InputTokens:N0} input, {usage.OutputTokens:N0} output, " +
            $"{usage.CacheReadInputTokens ?? 0:N0} cache read, {usage.CacheCreationInputTokens ?? 0:N0} cache write, stop={response.StopReason}");
    }
}
