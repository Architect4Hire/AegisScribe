using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.Gateway;

// Reuses AegisScribeAppFixture but under its own collection, which gives it its own
// DistributedApplication instance — the login/refresh/logout tests drive real OAuth round trips
// and shouldn't contend with AuthEndpointTests/TokenValidationTests over the same app instance.
[CollectionDefinition("AegisScribe API - Gateway")]
public class GatewayCollection : ICollectionFixture<AegisScribeAppFixture>;
