using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.PublicEdge;

// Reuses AegisScribeAppFixture but under its own collection, which gives it its own
// DistributedApplication instance — isolation matters here because the 429 test exhausts the
// anonymous-IP rate limit bucket, and sharing a fixture with AuthEndpointTests would starve it.
[CollectionDefinition("AegisScribe API - Public Edge")]
public class PublicEdgeCollection : ICollectionFixture<AegisScribeAppFixture>;
