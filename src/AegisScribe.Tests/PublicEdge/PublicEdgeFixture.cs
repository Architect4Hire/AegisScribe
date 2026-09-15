using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.PublicEdge;

// The one collection that needs an AppHost of its own. Every other collection shares one with a raised
// anonymous rate limit; these tests cannot use the raised value, because their subject IS the limit —
// one of them exhausts the bucket and asserts the 429 and its Retry-After.
[CollectionDefinition("AegisScribe API - Public Edge")]
public class PublicEdgeCollection : ICollectionFixture<TightRateLimitAppFixture>;
