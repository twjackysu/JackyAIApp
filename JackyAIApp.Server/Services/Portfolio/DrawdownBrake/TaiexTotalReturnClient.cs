namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <summary>
    /// What one month's fetch came to. <see cref="Success"/> with no points means TWSE answered
    /// and has nothing for that month; <see cref="Failure"/> means "do not trust or continue":
    /// a transport error, a non-JSON page, or a layout the parser could not read.
    /// </summary>
    public readonly record struct TaiexMonthResult(bool Success, IReadOnlyList<IndexClose> Points)
    {
        public static TaiexMonthResult Failure => new(false, []);
        public static TaiexMonthResult Ok(IReadOnlyList<IndexClose> points) => new(true, points);
    }

    public interface ITaiexTotalReturnClient
    {
        /// <summary>Every published daily close of the month, ascending. Never throws for an
        /// upstream problem — see <see cref="TaiexMonthResult"/>.</summary>
        Task<TaiexMonthResult> FetchMonthAsync(int year, int month, CancellationToken ct = default);
    }

    /// <summary>
    /// TWSE <c>rwd/zh/TAIEX/MFI94U</c>: one month of 發行量加權股價報酬指數 closes per request.
    ///
    /// The Boss's rule for this host is a request at most every 2 seconds; this keeps 3, and keeps
    /// only one request in flight process-wide (register it as a singleton). The gap is measured
    /// from the end of the previous request, so a slow response never shortens it. A bad answer
    /// from TWSE ends the caller's round rather than being retried — the same IP serves the app's
    /// TW stock quotes, and being blocked for hammering a rate limit would take those down too.
    /// For the same reason this client has no Polly retry: <c>AddReadOnlyResilience</c> would
    /// re-send a failed GET after about a second, inside the minimum gap and straight into a 429.
    /// The hourly refresh is the retry.
    /// </summary>
    public class TaiexTotalReturnClient(
        IHttpClientFactory httpClientFactory,
        ILogger<TaiexTotalReturnClient> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? minInterval = null) : ITaiexTotalReturnClient
    {
        internal const string HttpClientName = "TaiexTotalReturn";
        internal const string BaseUrl = "https://www.twse.com.tw/rwd/zh/TAIEX/MFI94U";

        /// <summary>Do not lower this: it is the margin on the Boss's 2-second rule.</summary>
        internal static readonly TimeSpan DefaultMinInterval = TimeSpan.FromSeconds(3);

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
        private readonly TimeSpan _minInterval = minInterval ?? DefaultMinInterval;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTimeOffset? _lastFinishedAt;

        public async Task<TaiexMonthResult> FetchMonthAsync(int year, int month, CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (_lastFinishedAt is { } last)
                {
                    var wait = _minInterval - (_time.GetUtcNow() - last);
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, _time, ct);
                }

                try
                {
                    var client = httpClientFactory.CreateClient(HttpClientName);
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}?date={year:0000}{month:00}01&response=json");
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                    req.Headers.TryAddWithoutValidation("Accept", "application/json");
                    using var resp = await client.SendAsync(req, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        logger.LogWarning("MFI94U {Year}-{Month:00}: HTTP {Status}", year, month, (int)resp.StatusCode);
                        return TaiexMonthResult.Failure;
                    }

                    var body = await resp.Content.ReadAsStringAsync(ct);
                    return TaiexTotalReturnParser.Parse(body, year, month, logger);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "MFI94U {Year}-{Month:00}: request failed", year, month);
                    return TaiexMonthResult.Failure;
                }
            }
            finally
            {
                _lastFinishedAt = _time.GetUtcNow();
                _gate.Release();
            }
        }
    }
}
