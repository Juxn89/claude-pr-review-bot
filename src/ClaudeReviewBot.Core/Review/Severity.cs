using System.Text.Json.Serialization;

namespace ClaudeReviewBot.Core.Review;

/// <summary>
/// Three buckets, declared in display order. An enum of strings, not an integer scale: the
/// structured-output schema dialect rejects numeric constraints like <c>maximum</c>, and three
/// named levels turned out to be the better model anyway.
/// </summary>
public enum Severity
{
    [JsonStringEnumMemberName("blocker")]
    Blocker,

    [JsonStringEnumMemberName("consider")]
    Consider,

    [JsonStringEnumMemberName("nit")]
    Nit,
}
