using System.Text.Json;
using ClaudeReviewBot.Core.Review;

namespace ClaudeReviewBot.Tests;

public sealed class FindingsSchemaTests
{
    private static readonly string[] UnsupportedKeywords =
    [
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "minLength", "maxLength", "pattern", "format", "minItems", "maxItems",
    ];

    [Fact]
    public void Every_object_forbids_additional_properties()
    {
        JsonElement schema = FindingsSchema.AsElement();

        foreach (JsonElement obj in Objects(schema))
        {
            Assert.True(obj.TryGetProperty("additionalProperties", out JsonElement ap) && ap.ValueKind == JsonValueKind.False,
                $"object without additionalProperties=false: {obj}");
        }
    }

    [Fact]
    public void Uses_no_keywords_the_structured_outputs_dialect_rejects()
    {
        string json = FindingsSchema.AsElement().GetRawText();

        foreach (string keyword in UnsupportedKeywords)
        {
            Assert.DoesNotContain($"\"{keyword}\"", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Severity_enum_matches_the_csharp_enum_names()
    {
        JsonElement severity = FindingsSchema.AsElement()
            .GetProperty("properties").GetProperty("findings").GetProperty("items").GetProperty("properties").GetProperty("severity");

        string[] allowed = [.. severity.GetProperty("enum").EnumerateArray().Select(e => e.GetString()!)];
        string[] fromEnum = [.. Enum.GetNames<Severity>().Select(n => n.ToLowerInvariant())];

        Assert.Equal(fromEnum, allowed);
    }

    [Fact]
    public void A_document_matching_the_schema_round_trips_through_FindingsJson()
    {
        const string json = """
            {"findings":[{"path":"a.cs","line":3,"severity":"blocker","comment":"x"}]}
            """;

        ReviewFindings parsed = FindingsJson.Parse(json);

        Finding finding = Assert.Single(parsed.Findings);
        Assert.Equal(new Finding("a.cs", 3, Severity.Blocker, "x"), finding);
        Assert.Contains("\"severity\": \"blocker\"", FindingsJson.Serialize(parsed), StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_findings_parse_to_the_shared_empty_instance()
    {
        Assert.Empty(FindingsJson.Parse("{\"findings\":[]}").Findings);
        Assert.Empty(FindingsJson.Parse("{}").Findings);
    }

    private static IEnumerable<JsonElement> Objects(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object")
            {
                yield return node;
            }

            foreach (JsonProperty property in node.EnumerateObject())
            {
                foreach (JsonElement child in Objects(property.Value))
                {
                    yield return child;
                }
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in node.EnumerateArray())
            {
                foreach (JsonElement child in Objects(item))
                {
                    yield return child;
                }
            }
        }
    }
}
