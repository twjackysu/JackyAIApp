using JackyAIApp.Server.Common;
using Microsoft.Extensions.DependencyInjection;
using TWStockLib.Models;
using TWStockLib.Services;

namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <summary>
    /// One reading of the TAIEX (加權指數, a price index) from TWSE's real-time service.
    /// </summary>
    /// <param name="TradeDate">The trading day the reading belongs to: the Taipei date of <paramref name="QuotedAt"/>.</param>
    /// <param name="QuotedAt">When the index was last computed (MIS <c>tlong</c>), in Taipei time.</param>
    /// <param name="Last">The index now (MIS <c>z</c>).</param>
    /// <param name="PreviousClose">The previous trading day's close of the same index (MIS <c>y</c>).</param>
    public sealed record MisIndexQuote(DateOnly TradeDate, DateTimeOffset QuotedAt, decimal Last, decimal PreviousClose);

    public interface IMisTaiexClient
    {
        /// <summary>The index reading now, or null if MIS gave no usable one. Never throws for an
        /// upstream problem; only the caller's own cancellation propagates.</summary>
        Task<MisIndexQuote?> GetAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// One request to TWSE's real-time service for the TAIEX (MIS code <c>t00</c>), made through
    /// TWStockLib like any listed stock's quote: <c>z</c> is <see cref="StockQuote.LastPrice"/>, <c>y</c>
    /// is <see cref="StockQuote.YesterdayClosingPrice"/> and <c>tlong</c> is <see cref="StockQuote.SyncTime"/>.
    /// The library does the HTTP and the parsing; this adds what only this feature knows — that the
    /// answer must be about the index, a plausible reading, and fresh.
    ///
    /// <para>It has no pacing of its own: <see cref="MisTaiexQuoteSource"/> is what keeps requests to a
    /// handful a minute for the whole site, so every call here must really go to MIS — hence
    /// <c>bypassCache: true</c>, since the library otherwise keeps each quote for 30 seconds, longer than
    /// the 10 seconds the refresh button promises. Whatever goes wrong is "no reading": the estimate is
    /// then simply absent, and the official signal is untouched.</para>
    ///
    /// <para>TWStockLib's <c>IStockMarketService</c> is scoped, so each call opens its own scope; the
    /// library's fetcher and cache underneath are singletons and shared all the same.</para>
    /// </summary>
    public class MisTaiexClient(
        IServiceScopeFactory scopeFactory,
        ILogger<MisTaiexClient> logger,
        TimeSpan? timeout = null) : IMisTaiexClient
    {
        /// <summary>MIS's code for the 加權指數 (the <c>t00</c> in <c>ex_ch=tse_t00.tw</c>).</summary>
        internal const string IndexSymbol = "t00";

        /// <summary>The largest one-day move believed, either way. The exchange halts at ±10% and the
        /// TAIEX's record is about ±9.7%, so a reading beyond this is bad data, not news.</summary>
        internal const decimal MaxMove = 0.15m;

        /// <summary>Ceiling on one reading. A person is waiting behind it (the refresh button, or a look
        /// that happens to be the one asking MIS again), and so is everyone who arrives meanwhile; the
        /// library's HTTP client allows far longer, with retries.</summary>
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        public async Task<MisIndexQuote?> GetAsync(CancellationToken ct = default)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var stocks = scope.ServiceProvider.GetRequiredService<IStockMarketService>();

                // The library's own request keeps going if we give up on it, but nobody waits for it.
                var result = await stocks.GetRealtimeQuoteAsync(IndexSymbol, MarketType.TSE, bypassCache: true, ct)
                    .WaitAsync(timeout ?? DefaultTimeout, ct);

                var quote = Read(result, out var rejection);
                if (quote is null && rejection is not null)
                    logger.LogWarning("MIS TAIEX: reading rejected: {Reason}", rejection);
                return quote;
            }
            // A caller walking away is not a failure; our own timeout (a TimeoutException) is.
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "MIS TAIEX: request failed or timed out");
                return null;
            }
        }

        /// <summary>The reading, or null: with a <paramref name="rejection"/> when something is wrong, without one
        /// when it is only that MIS has no index value yet (before the open, or in its first seconds).</summary>
        internal static MisIndexQuote? Read(StockResult<StockQuote> result, out string? rejection)
        {
            rejection = null;

            if (!result.IsSuccess || result.Value is not { } quote)
            {
                rejection = $"TWStockLib: {result.ErrorCode} {result.ErrorMessage}";
                return null;
            }

            // The answer must be about the index we asked for, not whatever came back.
            if (!string.Equals(quote.Symbol, IndexSymbol, StringComparison.OrdinalIgnoreCase))
            {
                rejection = $"the answer is for {quote.Symbol ?? "(no code)"}, not {IndexSymbol}";
                return null;
            }

            // "-" is MIS's word for "no trade yet"; the library reads it as no price.
            if (quote.LastPrice is not { } last)
                return null;
            if (last <= 0)
            {
                rejection = $"z (last) is not a usable price: {last}";
                return null;
            }

            // The library reads a missing or "-" y as 0.
            if (quote.YesterdayClosingPrice is not { } previous || previous <= 0)
            {
                rejection = $"y (previous close) is not a usable price: {quote.YesterdayClosingPrice?.ToString() ?? "(missing)"}";
                return null;
            }

            var move = Math.Abs(last / previous - 1);
            if (move > MaxMove)
            {
                rejection = $"z {last} vs y {previous} is a {move:P1} move (limit {MaxMove:P0})";
                return null;
            }

            // SyncTime is the UTC wall-clock time of tlong, with its Kind left unspecified.
            if (quote.SyncTime is not { } syncTime)
            {
                rejection = "tlong (epoch ms) is missing or unreadable";
                return null;
            }

            var quotedAt = new DateTimeOffset(DateTime.SpecifyKind(syncTime, DateTimeKind.Utc)).ToOffset(TaipeiTime.Offset);
            return new MisIndexQuote(DateOnly.FromDateTime(quotedAt.DateTime), quotedAt, last, previous);
        }
    }
}
