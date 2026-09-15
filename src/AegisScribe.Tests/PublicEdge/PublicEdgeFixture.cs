using AegisScribe.Tests.Auth;

namespace AegisScribe.Tests.PublicEdge;

// The one collection that still needs an AppHost of its own, and now for a sharper reason than before.
//
// Every other collection was separate because the API's 20-a-minute anonymous bucket is too tight for a
// test suite to share. That is now configuration, and the shared fixture raises it. These tests cannot
// use the raised value: their subject IS the limit — one of them exhausts the bucket and asserts the 429
// and its Retry-After — so they run against TightRateLimitAppFixture, which keeps the production number.
//
// Separate for a real reason rather than by default, which is the difference between isolation and cost.
[CollectionDefinition("AegisScribe API - Public Edge")]
public class PublicEdgeCollection : ICollectionFixture<TightRateLimitAppFixture>;
