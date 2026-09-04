using System.Globalization;
using System.Net;

namespace ClaudeReviewBot.Core.GitHub;

/// <summary>
/// Retries transient GitHub failures (429, 5xx, primary/secondary rate limits reported as a 403
/// with an exhausted quota, dropped connections), honouring <c>Retry-After</c> and
/// <c>x-ratelimit-reset</c> when present, exponential backoff with jitter otherwise.
/// Requests are cloned per attempt because an <see cref="HttpRequestMessage"/> can be sent once.
/// </summary>
public sealed class RetryHandler(HttpMessageHandler innerHandler, int maxAttempts = 4, TimeSpan? baseDelay = null, TimeProvider? timeProvider = null)
    : DelegatingHandler(innerHandler)
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    private readonly int _maxAttempts = maxAttempts >= 1 ? maxAttempts : throw new ArgumentOutOfRangeException(nameof(maxAttempts));
    private readonly TimeSpan _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        for (int attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                using HttpRequestMessage clone = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
                response = await base.SendAsync(clone, cancellationToken).ConfigureAwait(false);

                if (!ShouldRetry(response) || attempt >= _maxAttempts)
                {
                    return response;
                }
            }
            catch (HttpRequestException) when (attempt < _maxAttempts)
            {
                // transient transport failure; fall through to the delay
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < _maxAttempts)
            {
                // HttpClient timeout, not a caller cancellation
            }

            TimeSpan delay = DelayFor(response, attempt);
            response?.Dispose();
            await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ShouldRetry(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout)
        {
            return true;
        }

        // GitHub signals both primary and secondary rate limits as 403.
        return response.StatusCode == HttpStatusCode.Forbidden
            && (response.Headers.RetryAfter is not null
                || (response.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string>? remaining) && remaining.FirstOrDefault() == "0"));
    }

    private TimeSpan DelayFor(HttpResponseMessage? response, int attempt)
    {
        if (response?.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta)
            {
                return Clamp(delta);
            }

            if (retryAfter.Date is { } date)
            {
                return Clamp(date - _time.GetUtcNow());
            }
        }

        if (response is not null
            && response.Headers.TryGetValues("x-ratelimit-reset", out IEnumerable<string>? reset)
            && long.TryParse(reset.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long epochSeconds))
        {
            return Clamp(DateTimeOffset.FromUnixTimeSeconds(epochSeconds) - _time.GetUtcNow());
        }

        double exponential = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        double jitter = Random.Shared.NextDouble() * _baseDelay.TotalMilliseconds;
        return Clamp(TimeSpan.FromMilliseconds(exponential + jitter));
    }

    private static TimeSpan Clamp(TimeSpan delay) =>
        delay < TimeSpan.Zero ? TimeSpan.Zero : delay > MaxDelay ? MaxDelay : delay;

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpRequestMessage clone = new(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (KeyValuePair<string, object?> option in request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        if (request.Content is not null)
        {
            byte[] body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            ByteArrayContent content = new(body);
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }
}
