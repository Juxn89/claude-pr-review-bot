using System.Net;

namespace ClaudeReviewBot.Tests.Fakes;

/// <summary>Replays a queue of responses and records every request it saw, body included.</summary>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = [];

    public ScriptedHandler Enqueue(HttpStatusCode status, string body = "", Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(_ =>
        {
            HttpResponseMessage response = new(status) { Content = new StringContent(body) };
            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    public ScriptedHandler EnqueueThrow(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.ToString(), body));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response left for {request.Method} {request.RequestUri}");
        }

        HttpResponseMessage response = _responses.Dequeue()(request);
        response.RequestMessage = request;
        return response;
    }
}
