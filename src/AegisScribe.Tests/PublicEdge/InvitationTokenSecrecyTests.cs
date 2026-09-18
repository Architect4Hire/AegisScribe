using System.Collections.Concurrent;
using System.Net.Http.Json;
using AegisScribe.ApiService;
using AegisScribe.Domain.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AegisScribe.Tests.PublicEdge;

// 8.3b: "the token round-trips ... without being written to ... a log line."
//
// What that CAN mean, and what this pins: no code in this application writes an invitation token into
// a log message, a log scope, or a structured field — including on the paths where the token is
// invalid, which are the ones most likely to tempt a "couldn't find invitation {Token}" line.
//
// This test earned its keep immediately: with the token in the route, ASP.NET Core's own RequestPath
// logging scope stamped it onto every entry the request produced, and onto OpenTelemetry's url.path
// with it. No discipline inside a controller could have fixed that, so both endpoints became POSTs
// carrying the token in the body.
//
// What it still cannot mean: the token is in the SPA's /join/:token URL and in the gateway's
// returnUrl, because a link somebody clicks has to be a link — so it is in the browser's address bar
// and in whatever access log fronts those two. What answers that is the short expiry, the single use
// and the revoke, not a logging rule. Saying so here rather than letting the phrase imply a guarantee
// nobody can keep.
//
// In-process rather than through the Aspire fixture, because capturing the host's own ILogger output
// means owning the host. Same shape as OpenApiContractTests, and the same reason.
public class InvitationTokenSecrecyTests
{
    // Distinctive enough that a substring match cannot pass by accident, and shaped like the real
    // thing (base64url, no separators) so nothing splits it before it reaches a sink.
    private const string ProbeToken = "zzUNMISTAKABLEtokenPROBE1234567890abcdefgh";

    [Theory]
    [InlineData("/api/v1/invitations/preview")]
    [InlineData("/api/v1/invitations/accept")]
    public async Task NoLogLineContainsTheToken(string path)
    {
        var sink = new CapturingLoggerProvider();

        using var factory = CreateFactory(sink);
        using var client = factory.CreateClient();

        // The unauthenticated, unknown-token path: a 404 from preview and a 401 from accept. Both are
        // failure paths, which is exactly where a diagnostic log line would have been added.
        //
        // This test is what made the endpoints POSTs. With the token in the route, ASP.NET Core's own
        // RequestPath scope put it on EVERY entry the request wrote — the framework, not our code, and
        // no amount of discipline in a controller would have fixed it.
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { token = ProbeToken }),
        };

        await client.SendAsync(request);

        var offending = sink.Entries
            .Where(entry => entry.Contains(ProbeToken, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            offending.Count == 0,
            $"The invitation token reached {offending.Count} log entries, e.g.: {offending.FirstOrDefault()}");
    }

    private static WebApplicationFactory<TestEntryPoint> CreateFactory(ILoggerProvider sink) =>
        new WebApplicationFactory<TestEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var toRemove = services
                    .Where(d => (d.ServiceType.FullName ?? d.ServiceType.ToString()).Contains(nameof(AegisScribeDbContext)))
                    .ToList();

                foreach (var descriptor in toRemove)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AegisScribeDbContext>(options =>
                    options.UseInMemoryDatabase(Guid.NewGuid().ToString()).UseOpenIddict());

                // Trace, so nothing hides below the configured minimum — a token leaked at Debug is
                // still a token in a log file on somebody's machine.
                services.AddLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddProvider(sink);
                });
            });
        });

    // Captures the formatted message AND the state, because a structured field never appears in the
    // message text — "{Token}" renders as a placeholder unless something formats it, and the value
    // would still be in the payload a sink serializes.
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentBag<string> _entries = [];

        public IEnumerable<string> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentBag<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                entries.Add(state.ToString() ?? string.Empty);

                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.Add(formatter(state, exception));
                entries.Add(state?.ToString() ?? string.Empty);

                if (exception is not null)
                {
                    entries.Add(exception.ToString());
                }
            }
        }
    }
}
