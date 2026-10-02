using System.Collections.Concurrent;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// De-duplicates concurrent cache-miss fetches for the same key: the first caller starts
    /// <paramref name="factory"/> and every other concurrent caller for the same key awaits that
    /// same in-flight task instead of issuing its own redundant upstream call.
    ///
    /// <paramref name="factory"/> must not take a per-caller <see cref="CancellationToken"/> — it
    /// is shared by every caller currently waiting on the key, so one caller cancelling must never
    /// cancel the fetch for the others. Pass <see cref="CancellationToken.None"/> (or a token
    /// scoped to the whole app, not a single request) to whatever the factory calls internally.
    /// The per-caller <paramref name="ct"/> here only stops *this* caller from waiting any longer;
    /// it does not touch the shared task.
    ///
    /// Keys are a single flat namespace shared by every call site (matching how they already share
    /// one <c>IMemoryCache</c> instance), so callers must keep using the same cache-key prefixing
    /// convention to avoid collisions.
    /// </summary>
    public static class SingleFlight
    {
        private static readonly ConcurrentDictionary<string, object> InFlight = new();

        public static Task<T> RunAsync<T>(string key, Func<Task<T>> factory, CancellationToken ct)
        {
            var lazy = (Lazy<Task<T>>)InFlight.GetOrAdd(key, _ => new Lazy<Task<T>>(() => RunOnceAsync(key, factory)));
            return lazy.Value.WaitAsync(ct);
        }

        private static async Task<T> RunOnceAsync<T>(string key, Func<Task<T>> factory)
        {
            try
            {
                return await factory().ConfigureAwait(false);
            }
            finally
            {
                InFlight.TryRemove(key, out _);
            }
        }
    }
}
