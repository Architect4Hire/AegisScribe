namespace AegisScribe.ApiService;

// Marker type for WebApplicationFactory<TestEntryPoint> in AegisScribe.Tests. The Program class
// generated from Program.cs's top-level statements is internal by default, and — since the test
// project also references AegisScribe.AppHost, which has its own top-level-statements Program in
// the same global namespace — making it public wouldn't be enough to disambiguate it either. A
// dedicated, namespaced marker sidesteps both problems; WebApplicationFactory only needs a public
// type from the right assembly to locate it.
public sealed class TestEntryPoint;
