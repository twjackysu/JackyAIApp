namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Waits for an operation at most a fixed time, even when the operation ignores cancellation.
    /// </summary>
    public static class TimeBox
    {
        /// <summary>
        /// Runs <paramref name="operation"/> and returns its result, or <c>TimedOut = true</c> once
        /// <paramref name="timeout"/> passes. On timeout the operation's token is cancelled, but the wait
        /// ends regardless — some callees (the ADX connector) never look at the token — and the operation
        /// is left to finish in the background with any fault observed. Only the caller's own
        /// <paramref name="ct"/> makes this throw.
        /// </summary>
        public static async Task<(bool TimedOut, T? Result)> RunAsync<T>(
            Func<CancellationToken, Task<T>> operation, TimeSpan timeout, CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var task = operation(timeoutCts.Token);
            if (await Task.WhenAny(task, Task.Delay(timeout, ct)) == task)
                return (false, await task);

            ct.ThrowIfCancellationRequested();
            timeoutCts.Cancel();
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return (true, default);
        }
    }
}
