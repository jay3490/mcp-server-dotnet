using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace EngineeringMcp.Tests.Support;

/// <summary>One captured log event with its template values and scope values merged.</summary>
public sealed record CapturedLog(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties);

/// <summary>Keeps every log event in memory so tests can assert on them, including the audit trail.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<CapturedLog> Entries { get; } = new();

    public IEnumerable<CapturedLog> ForCategory(string category) => Entries.Where(e => e.Category == category);

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            provider._scopes.ForEachScope(
                (scope, props) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (var (key, value) in pairs)
                        {
                            props[key] = value;
                        }
                    }
                },
                properties);

            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var (key, value) in values)
                {
                    properties[key] = value;
                }
            }

            provider.Entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), properties));
        }
    }
}
