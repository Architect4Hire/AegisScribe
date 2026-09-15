using System.Net;
using System.Net.Http.Headers;

namespace AegisScribe.Domain.Integration.Blizzard;

// Puts the bearer token on every outbound Blizzard request, so no gateway method has to remember to —
// and so there is nowhere for a token to end up appended to a URL instead. Blizzard disallowed the
// query-parameter form in 2024 and the repo's secret-guard hook blocks it outright (external.md).
public sealed class BlizzardAuthHandler(IBlizzardTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetTokenAsync(cancellationToken);

        if (token is null)
        {
            // Short-circuit rather than throw: missing credentials degrade (external.md). BlizzardGateway
            // checks IsConfigured before it builds a request at all, so in practice this is unreachable.
            // It exists so that a future gateway method which forgets that guard fails here instead of
            // sending an unauthenticated request to Blizzard and reading the 401 as real data.
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                ReasonPhrase = "Blizzard credentials are not configured.",
            };
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
