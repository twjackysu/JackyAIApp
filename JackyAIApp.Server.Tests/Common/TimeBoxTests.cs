using System.Diagnostics;
using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// The NL agent reads every String column's distinct values before planning. On a big source that
    /// query can run up to the SQL connector's 120 s command timeout, and the ADX connector ignores the
    /// cancellation token altogether. These tests pin that the wait still ends on time.
    /// </summary>
    public class TimeBoxTests
    {
        [Fact]
        public async Task FastOperation_ReturnsItsResult()
        {
            var (timedOut, result) = await TimeBox.RunAsync(_ => Task.FromResult(42), TimeSpan.FromSeconds(5), default);

            Assert.False(timedOut);
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task OperationIgnoringCancellation_StillTimesOutOnTime()
        {
            var never = new TaskCompletionSource<int>(); // like ADX: never completes, never looks at the token
            var watch = Stopwatch.StartNew();

            var (timedOut, _) = await TimeBox.RunAsync(_ => never.Task, TimeSpan.FromMilliseconds(100), default);

            Assert.True(timedOut);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
        }

        [Fact]
        public async Task OnTimeout_OperationTokenIsCancelled()
        {
            CancellationToken seen = default;

            await TimeBox.RunAsync(async token => { seen = token; await Task.Delay(Timeout.Infinite, token); return 0; },
                TimeSpan.FromMilliseconds(50), default);

            Assert.True(seen.IsCancellationRequested);
        }

        [Fact]
        public async Task CallerCancellation_Throws()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                TimeBox.RunAsync(_ => new TaskCompletionSource<int>().Task, TimeSpan.FromSeconds(30), cts.Token));
        }
    }
}
