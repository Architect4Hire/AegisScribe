using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AegisScribe.MigrationService;

// First-party clients only — see CLAUDE.md Scope: no client registration UI, no third-party
// consumers. ConsentType.Implicit throughout because there is no consent screen for any of them.
public static class OpenIddictClientSeeder
{
    public static async Task SeedClientsAsync(IServiceProvider services, IConfiguration configuration)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        var gatewayUrl = configuration["services:gateway:https:0"] ?? configuration["services:gateway:http:0"]
            ?? throw new InvalidOperationException("Gateway endpoint is not configured for client seeding.");

        await CreateOrUpdateAsync(manager, new OpenIddictApplicationDescriptor
            {
                ApplicationType = ApplicationTypes.Web,
                ClientId = "aegisscribe-bff",
                ClientSecret = configuration["Oidc:BffClientSecret"],
                ClientType = ClientTypes.Confidential,
                ConsentType = ConsentTypes.Implicit,
                DisplayName = "AegisScribe Gateway (BFF)",
                RedirectUris = { new Uri($"{gatewayUrl}/auth/callback") },
                PostLogoutRedirectUris = { new Uri($"{gatewayUrl}/auth/signout") },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.Revocation,
                    Permissions.Endpoints.EndSession,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    // openid is what makes this an OIDC (not bare OAuth2) code exchange — the
                    // gateway's OpenIdConnect handler expects an id_token back. offline_access is
                    // what makes OpenIddict issue a refresh token at all for this client (1B.6).
                    Permissions.Prefixes.Scope + Scopes.OpenId,
                    Permissions.Prefixes.Scope + Scopes.OfflineAccess,
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange },
            });

        // Registered as Native so OpenIddict applies the relaxed redirect_uri validation a
        // custom-scheme mobile redirect needs. No ClientSecret in any form — a public client.
        await CreateOrUpdateAsync(manager, new OpenIddictApplicationDescriptor
            {
                ApplicationType = ApplicationTypes.Native,
                ClientId = "aegisscribe-mobile",
                ClientType = ClientTypes.Public,
                ConsentType = ConsentTypes.Implicit,
                DisplayName = "AegisScribe Mobile",
                RedirectUris = { new Uri("aegisscribe://auth/callback") },
                PostLogoutRedirectUris = { new Uri("aegisscribe://auth/signout") },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.Revocation,
                    Permissions.Endpoints.EndSession,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    // mobile.md's real login request asks for "openid profile offline_access";
                    // without the matching scope permissions here OpenIddict rejects the
                    // authorization request as invalid_scope before a code is ever issued.
                    Permissions.Prefixes.Scope + Scopes.OpenId,
                    Permissions.Prefixes.Scope + Scopes.Profile,
                    Permissions.Prefixes.Scope + Scopes.OfflineAccess,
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange },
            });

        // Service-to-service only — never issued against a user's token. No redirect URIs, no
        // ApplicationType: there is no interactive flow to validate a redirect against. A resource
        // indicator (RFC 8707) has to be an absolute URI, and OpenIddict only mints against one the
        // *client* has permission for — so two are granted here: one this API's own AddAudiences()
        // would accept, and one it deliberately would not, letting a test request a resource that's
        // valid at the protocol layer but still wrong for this resource server (1B.5's four
        // token-validation scenarios). Neither is used unless a caller names a "resource" parameter
        // explicitly (TokenEndpoints.cs); the default token audience is the plain string
        // "aegisscribe-api", matching AddAudiences() in Program.cs.
        await CreateOrUpdateAsync(manager, new OpenIddictApplicationDescriptor
            {
                ClientId = "aegisscribe-ops",
                ClientSecret = configuration["Oidc:OpsClientSecret"],
                ClientType = ClientTypes.Confidential,
                ConsentType = ConsentTypes.Implicit,
                DisplayName = "AegisScribe Ops (service-to-service)",
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Resource + "https://api.aegisscribe.example/",
                    Permissions.Prefixes.Resource + "https://wrong-audience.aegisscribe.example/",
                },
            });
    }

    private static async Task CreateOrUpdateAsync(
        IOpenIddictApplicationManager manager, OpenIddictApplicationDescriptor descriptor)
    {
        var existing = await manager.FindByClientIdAsync(descriptor.ClientId!);
        if (existing is null)
        {
            await manager.CreateAsync(descriptor);
        }
        else
        {
            await manager.UpdateAsync(existing, descriptor);
        }
    }
}
