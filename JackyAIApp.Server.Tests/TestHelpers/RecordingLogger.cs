using Microsoft.Extensions.Logging;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>Keeps each log call's level, rendered message and structured properties (the
    /// <c>{Name}</c> placeholders of the template), which is what ends up in Application Insights'
    /// <c>customDimensions</c>.</summary>
    internal sealed class RecordingLogger<T> : ILogger<T>
    {
        internal sealed record Entry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties);

        private readonly object _gate = new();
        private readonly List<Entry> _entries = [];

        public IReadOnlyList<Entry> Entries
        {
            get { lock (_gate) return [.. _entries]; }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                foreach (var pair in pairs) properties[pair.Key] = pair.Value;
            lock (_gate) _entries.Add(new Entry(logLevel, formatter(state, exception), properties));
        }
    }
}
