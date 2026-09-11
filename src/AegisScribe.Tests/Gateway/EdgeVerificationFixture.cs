using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.Gateway;

// Reuses AegisScribeAppFixture but under its own collection/AppHost instance, the same reason
// HeaderSanitisationFixture/GatewayFixture exist: 1B.9's edge-verification tests add roughly 17 more
// anonymous-IP-bucketed requests (registrations, direct authorize/token/revoke calls) — enough to
// blow through the deliberately tight 20/min bucket (backend.md -> "The API's public edge") if they
// shared GatewayOAuthTests' collection.
[CollectionDefinition("AegisScribe API - Edge Verification")]
public class EdgeVerificationCollection : ICollectionFixture<AegisScribeAppFixture>;
