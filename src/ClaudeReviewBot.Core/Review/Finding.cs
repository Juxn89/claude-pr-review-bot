using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeReviewBot.Core.Review;

/// <summary>One thing the reviewer wants to say, anchored to a new-file line number.</summary>
public sealed record Finding(string Path, int Line, Severity Severity, string Comment);

/// <summary>The whole answer, as Claude returns it: a document, not a sequence of actions.</summary>
public sealed record ReviewFindings(IReadOnlyList<Finding> Findings)
{
    public static ReviewFindings Empty { get; } = new([]);
}

public static class FindingsJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    /// <summary>Parses the model's structured output (or a fixture file with the same shape).</summary>
    public static ReviewFindings Parse(string json)
    {
        ReviewFindings? parsed = JsonSerializer.Deserialize<ReviewFindings>(json, Options);
        return parsed is null || parsed.Findings is null ? ReviewFindings.Empty : parsed;
    }

    public static string Serialize(ReviewFindings findings) => JsonSerializer.Serialize(findings, Options);
}
