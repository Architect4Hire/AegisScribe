using System.Net;
using Microsoft.Extensions.Logging;

namespace AegisScribe.Domain.Integration.Blizzard;

// Takes a lease for every outbound Blizzard call, and turns a 429 into a shared backoff window.
//
// Registered as the INNERMOST handler, and that placement is the whole point: the standard resilience
// handler treats 429 as transient and retries up to three times, and those retries are real HTTP calls
// against a contractual cap. A lease taken up in a gateway method would count one call and spend four.
// It also makes "every gateway method takes a lease" structurally true rather than a convention each
// new method has to remember.
//
// The division of labour with the resilience handler: it owns *retrying*, this owns *when a retry is
// allowed to leave*. Nothing here retries, so there is no second retry loop to compound with Polly's.
public sealed class BlizzardRateLimitHandler(
    IBlizzardRateLimiter rateLimiter,
    TimeProvider timeProvider,
    ILogger<BlizzardRateLimitHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var lease = await rateLimiter.AcquireAsync(cancellationToken);

        if (!lease.IsAcquired)
        {
            // Self-throttled: the budget is spent or we are inside a backoff window. Short-circuit
            // rather than throw, so the DataLayer falls back to stored data as it does for any other
            // unavailable answer.
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                ReasonPhrase = "The Blizzard rate limit budget is exhausted.",
            };
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.TooManyRequests)
        {
            // A 429 means our accounting disagrees with Blizzard's — a shared client id, or a burst we
            // mis-sized. Their hint is better information than our arithmetic, so it wins.
            rateLimiter.RegisterThrottled(ReadRetryAfter(response));
        }

        return response;
    }

    // Retry-After comes in two forms (RFC 9110). Blizzard sends delta-seconds, but the HTTP-date form is
    // legal and a proxy in the path may rewrite it, so both are read.
    private TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var remaining = date - timeProvider.GetUtcNow();

            // A date already in the past means "you may retry now", not "wait forever".
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        logger.LogWarning("Blizzard returned 429 with a Retry-After header that could not be read.");

        return null;
    }
}
