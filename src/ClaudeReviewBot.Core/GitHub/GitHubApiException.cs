using System.Net;

namespace ClaudeReviewBot.Core.GitHub;

public sealed class GitHubApiException(HttpStatusCode statusCode, string method, string url, string responseBody)
    : Exception($"GitHub API {method} {url} returned {(int)statusCode} {statusCode}: {Truncate(responseBody)}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string ResponseBody { get; } = responseBody;

    private static string Truncate(string body) =>
        body.Length <= 500 ? body : string.Concat(body.AsSpan(0, 500), "...");
}
