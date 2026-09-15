using System.Net;

namespace AegisScribe.Tests.Infrastructure;

// The first fake HttpMessageHandler in this repo — nothing needed one before the Blizzard gateway (6.1).
// It records every request it is asked to send and answers from a script, so a test can assert on the
// URL, the namespace, the headers and, importantly, how many requests were made at all.
//
// Requests are recorded before the response is produced, so the count is accurate even for a script that
// throws.
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, Task<HttpResponseMessage>> _respond;
    private readonly List<RecordedRequest> _requests = [];
    private readonly Lock _gate = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) =>
        _respond = respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    // Every request seen so far, in order. A snapshot, so enumerating it while requests are in flight is
    // safe — which the single-flight test needs.
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public int RequestCount
    {
        get
        {
            lock (_gate)
            {
                return _requests.Count;
            }
        }
    }

    public RecordedRequest SingleRequest => Assert.Single(Requests);

    // Always answers with the same status and body.
    public static StubHttpMessageHandler Returning(HttpStatusCode statusCode, string? content = null) =>
        new(_ => Respond(statusCode, content));

    // Answers differently per attempt (1-based) without the caller having to wrap each response in a Task —
    // a separate factory rather than a constructor overload, because a lambda whose body only throws is
    // convertible to both the sync and the async delegate and would be ambiguous.
    public static StubHttpMessageHandler Scripted(Func<HttpRequestMessage, int, HttpResponseMessage> respond) =>
        new((request, attempt) => Task.FromResult(respond(request, attempt)));

    // Answers a token mint with a well-formed client-credentials response.
    public static StubHttpMessageHandler ReturningToken(string accessToken, int expiresInSeconds = 86_399) =>
        new(_ => RespondWithToken(accessToken, expiresInSeconds));

    public static HttpResponseMessage Respond(HttpStatusCode statusCode, string? content = null) =>
        new(statusCode)
        {
            Content = new StringContent(content ?? string.Empty, System.Text.Encoding.UTF8, "application/json"),
        };

    // Blizzard's documented client-credentials response shape
    // (references/blizzard-endpoints.md -> "Auth").
    public static HttpResponseMessage RespondWithToken(string accessToken, int expiresInSeconds = 86_399) =>
        Respond(
            HttpStatusCode.OK,
            $$"""{"access_token":"{{accessToken}}","token_type":"bearer","expires_in":{{expiresInSeconds}}}""");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Captured now rather than holding on to the live HttpRequestMessage: HttpClient disposes the
        // request and its content once the call completes, so a test asserting afterwards would be
        // reading freed state. Read before taking the lock — this is the one part that has to await.
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            body);

        int attempt;

        lock (_gate)
        {
            _requests.Add(recorded);
            attempt = _requests.Count;
        }

        return await _respond(request, attempt);
    }

    public sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? Body)
    {
        public string Query => RequestUri?.Query ?? string.Empty;

        public string AbsoluteUri => RequestUri?.AbsoluteUri ?? string.Empty;
    }
}
