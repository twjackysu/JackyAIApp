namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    public interface IMisTaiexClient
    {
        /// <summary>The index reading now, or null if MIS gave no usable one. Never throws for an
        /// upstream problem; only the caller's own cancellation propagates.</summary>
        Task<MisIndexQuote?> GetAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// One request to TWSE's real-time service for the TAIEX (<c>tse_t00.tw</c>), through the
    /// shared read-only client (retry + circuit breaker; it is an idempotent GET). It has no
    /// pacing of its own: <see cref="MisTaiexQuoteSource"/> is what keeps requests to a handful a
    /// minute for the whole site. Whatever goes wrong is "no reading" — the estimate is then
    /// simply absent, and the official signal is untouched.
    /// </summary>
    public class MisTaiexClient(
        IHttpClientFactory httpClientFactory,
        ILogger<MisTaiexClient> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? timeout = null) : IMisTaiexClient
    {
        internal const string HttpClientName = "ExternalReadOnly";
        internal const string BaseUrl = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp";

        /// <summary>Ceiling on one reading, retries included. A person is waiting behind it (the refresh
        /// button, or a look that happens to be the one asking MIS again), and so is everyone who arrives
        /// meanwhile; the default HttpClient timeout (100 s) would hold them far too long.</summary>
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        public async Task<MisIndexQuote?> GetAsync(CancellationToken ct = default)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout ?? DefaultTimeout);
            try
            {
                var client = httpClientFactory.CreateClient(HttpClientName);
                // The trailing `_` is the millisecond clock, the page's own cache-buster.
                var url = $"{BaseUrl}?ex_ch=tse_{MisTaiexParser.IndexCode}.tw&json=1&delay=0&_={_time.GetUtcNow().ToUnixTimeMilliseconds()}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var resp = await client.SendAsync(req, limit.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    logger.LogWarning("MIS TAIEX: HTTP {Status}", (int)resp.StatusCode);
                    return null;
                }

                var body = await resp.Content.ReadAsStringAsync(limit.Token);
                var quote = MisTaiexParser.Parse(body, out var rejection);
                if (quote is null)
                    logger.LogWarning("MIS TAIEX: reading rejected: {Reason}", rejection);
                return quote;
            }
            // A caller walking away is not a failure; our own timeout (also an OperationCanceledException) is.
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "MIS TAIEX: request failed");
                return null;
            }
        }
    }
}
