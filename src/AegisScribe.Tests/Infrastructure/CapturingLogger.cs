using Microsoft.Extensions.Logging;

namespace AegisScribe.Tests.Infrastructure;

// Records what was logged, so a test can assert that something was *said* — which for a degraded external
// integration is the behaviour, not a side effect of it.
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<Entry> _entries = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public IEnumerable<Entry> WithLevel(LogLevel level) => Entries.Where(entry => entry.Level == level);

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
        }
    }

    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
