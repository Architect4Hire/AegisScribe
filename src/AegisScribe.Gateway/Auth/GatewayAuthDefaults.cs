namespace AegisScribe.Gateway.Auth;

// Shared between Program.cs's auth wiring and the pieces (RefreshCoordinator, tests) that need to
// agree on the exact cookie name and client id without duplicating literals.
public static class GatewayAuthDefaults
{
    // __Secure- (not __Host-, which forbids Domain) — gateway.md's "Cookies across subdomains".
    public const string CookieName = "__Secure-aegisscribe";
    public const string ClientId = "aegisscribe-bff";
    public const string ApiBackchannelClient = "api-backchannel";
}
