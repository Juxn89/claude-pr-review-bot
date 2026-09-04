using System.Text.Json;

namespace ClaudeReviewBot.Core.Review;

/// <summary>
/// The JSON Schema Claude's response is constrained to. The structured-outputs dialect is a
/// subset: every object needs <c>additionalProperties: false</c>, and <c>minimum</c>,
/// <c>maximum</c>, <c>minLength</c> and friends are rejected with a 400, hence the enum.
/// </summary>
public static class FindingsSchema
{
    public static Dictionary<string, JsonElement> Create() => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "findings" }),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            findings = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "path", "line", "severity", "comment" },
                    properties = new
                    {
                        path = new { type = "string", description = "Repository-relative path of the file" },
                        line = new { type = "integer", description = "Line number in the new version of the file; must be a line the diff adds" },
                        severity = new { type = "string", @enum = new[] { "blocker", "consider", "nit" } },
                        comment = new { type = "string", description = "What is wrong, why it matters, what to do instead" },
                    },
                },
            },
        }),
    };

    public static JsonElement AsElement() => JsonSerializer.SerializeToElement(Create());
}
