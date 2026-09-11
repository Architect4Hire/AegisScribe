using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.Gateway;

// Reuses AegisScribeAppFixture but under its own collection/AppHost instance, the same reason
// GatewayFixture exists: these tests drive their own full login round trips and would otherwise
// share GatewayOAuthTests' 20/min anonymous-IP rate-limit budget (backend.md -> "The API's public
// edge") on the same API process, which is tight enough that the combined suite trips it.
[CollectionDefinition("AegisScribe API - Header Sanitisation")]
public class HeaderSanitisationCollection : ICollectionFixture<AegisScribeAppFixture>;
