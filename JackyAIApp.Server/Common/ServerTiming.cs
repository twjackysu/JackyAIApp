using System.Diagnostics;
using System.Globalization;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Stage timings for one request, sent back as a <c>Server-Timing</c> header so the browser's
    /// network panel (a request's Timing tab) shows where a slow request spent its time.
    ///
    /// Production logs can't be read (Application Insights receives nothing), so this is how a slow
    /// endpoint gets diagnosed: open DevTools on the live site and read the header. Scoped, so one
    /// per request; safe to record from tasks running in parallel.
    /// </summary>
    public sealed class ServerTiming
    {
        public const string HeaderName = "Server-Timing";

        private readonly List<string> _entries = [];

        public async Task<T> MeasureAsync<T>(string name, Func<Task<T>> work, Func<T, string>? describe = null)
        {
            var sw = Stopwatch.StartNew();
            var result = await work();
            Add(name, sw.Elapsed, describe?.Invoke(result));
            return result;
        }

        public async Task MeasureAsync(string name, Func<Task> work)
        {
            var sw = Stopwatch.StartNew();
            await work();
            Add(name, sw.Elapsed);
        }

        public void Add(string name, TimeSpan elapsed, string? description = null)
        {
            var entry = $"{name};dur={elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}";
            if (!string.IsNullOrEmpty(description))
                entry += $";desc=\"{Printable(description)}\"";
            lock (_entries) _entries.Add(entry);
        }

        /// <summary>The header value, or null when nothing was recorded.</summary>
        public string? ToHeaderValue()
        {
            lock (_entries) return _entries.Count == 0 ? null : string.Join(", ", _entries);
        }

        // A header value has to be ASCII (Kestrel rejects the response otherwise), and a desc is a
        // quoted string, so quotes and backslashes can't appear unescaped.
        private static string Printable(string s) =>
            new(s.Where(c => c is >= ' ' and <= '~' and not '"' and not '\\').ToArray());
    }
}
