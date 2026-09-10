using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AegisScribe.Tests.Auth;

/// <summary>
/// Boots the real AppHost once for the collection so integration tests exercise the actual
/// distributed app (real SQL Server, real Identity wiring) rather than a fake. The SQL/Redis
/// containers are ContainerLifetime.Persistent, so this attaches to whatever is already running
/// locally instead of spinning up new ones.
/// </summary>
public class AegisScribeAppFixture : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    public DistributedApplication App { get; private set; } = null!;
    public HttpClient ApiClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AegisScribe_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        appHost.Services.AddLogging(logging => logging
            .AddFilter("Default", LogLevel.Warning)
            .AddFilter("Aspire.Hosting.Dcp", LogLevel.Warning));

        App = await appHost.BuildAsync().WaitAsync(DefaultTimeout);
        await App.StartAsync().WaitAsync(DefaultTimeout);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("api").WaitAsync(DefaultTimeout);

        ApiClient = App.CreateHttpClient("api");
    }

    public async Task DisposeAsync()
    {
        ApiClient?.Dispose();
        await App.DisposeAsync();
    }
}

[CollectionDefinition("AegisScribe API")]
public class AegisScribeApiCollection : ICollectionFixture<AegisScribeAppFixture>;
