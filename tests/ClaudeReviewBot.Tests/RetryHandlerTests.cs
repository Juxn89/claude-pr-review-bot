using System.Net;
using ClaudeReviewBot.Core.GitHub;
using ClaudeReviewBot.Tests.Fakes;

namespace ClaudeReviewBot.Tests;

public sealed class RetryHandlerTests
{
    private static HttpClient Client(ScriptedHandler inner, int maxAttempts = 4) =>
        new(new RetryHandler(inner, maxAttempts, baseDelay: TimeSpan.Zero)) { BaseAddress = new Uri("https://api.github.example/") };

    [Fact]
    public async Task Retries_transient_statuses_then_succeeds()
    {
        ScriptedHandler inner = new ScriptedHandler()
            .Enqueue(HttpStatusCode.ServiceUnavailable)
            .Enqueue(HttpStatusCode.BadGateway)
            .Enqueue(HttpStatusCode.OK, "{}");

        using HttpResponseMessage response = await Client(inner).GetAsync("repos/x/y");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Requests.Count);
    }

    [Fact]
    public async Task Does_not_retry_client_errors()
    {
        ScriptedHandler inner = new ScriptedHandler().Enqueue(HttpStatusCode.UnprocessableEntity, "line not in diff");

        using HttpResponseMessage response = await Client(inner).GetAsync("repos/x/y");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Gives_up_after_max_attempts_and_returns_the_last_response()
    {
        ScriptedHandler inner = new ScriptedHandler()
            .Enqueue(HttpStatusCode.TooManyRequests)
            .Enqueue(HttpStatusCode.TooManyRequests);

        using HttpResponseMessage response = await Client(inner, maxAttempts: 2).GetAsync("repos/x/y");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task Treats_a_403_with_exhausted_rate_limit_as_retryable()
    {
        ScriptedHandler inner = new ScriptedHandler()
            .Enqueue(HttpStatusCode.Forbidden, "rate limited", r =>
            {
                r.Headers.Add("x-ratelimit-remaining", "0");
                r.Headers.Add("x-ratelimit-reset", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            })
            .Enqueue(HttpStatusCode.OK);

        using HttpResponseMessage response = await Client(inner).GetAsync("repos/x/y");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task A_plain_403_is_a_real_permission_error_and_is_not_retried()
    {
        ScriptedHandler inner = new ScriptedHandler().Enqueue(HttpStatusCode.Forbidden, "Resource not accessible by integration");

        using HttpResponseMessage response = await Client(inner).GetAsync("repos/x/y");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Post_bodies_survive_a_retry()
    {
        ScriptedHandler inner = new ScriptedHandler()
            .Enqueue(HttpStatusCode.GatewayTimeout)
            .Enqueue(HttpStatusCode.Created);

        using StringContent content = new("{\"event\":\"COMMENT\"}", System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Client(inner).PostAsync("repos/x/y/pulls/1/reviews", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.All(inner.Requests, r => Assert.Equal("{\"event\":\"COMMENT\"}", r.Body));
    }

    [Fact]
    public async Task Retries_connection_failures()
    {
        ScriptedHandler inner = new ScriptedHandler()
            .EnqueueThrow(new HttpRequestException("connection reset"))
            .Enqueue(HttpStatusCode.OK);

        using HttpResponseMessage response = await Client(inner).GetAsync("repos/x/y");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_retried()
    {
        ScriptedHandler inner = new ScriptedHandler().Enqueue(HttpStatusCode.OK);
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(inner).GetAsync("repos/x/y", cts.Token));
    }
}
