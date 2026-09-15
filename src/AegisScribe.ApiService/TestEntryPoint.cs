namespace AegisScribe.ApiService;

// Marker type for WebApplicationFactory<TestEntryPoint>. The generated Program class is internal, and
// making it public would still collide with AppHost's own top-level-statements Program in the same
// global namespace. WebApplicationFactory only needs a public type from the right assembly.
public sealed class TestEntryPoint;
