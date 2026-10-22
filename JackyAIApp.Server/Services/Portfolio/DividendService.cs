using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data.Models.Portfolio;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>One dividend/distribution event: how much was paid per share/unit, and when it went ex-dividend.</summary>
    /// <param name="ExDate">The ex-dividend date — holding the position on this date is what earns the payout.</param>
    /// <param name="AmountPerUnit">Cash amount paid per share/unit, in <paramref name="Currency"/>, before tax.</param>
    /// <param name="Currency">
    /// For stocks, the authoritative pricing currency reported by Yahoo. For funds, always empty —
    /// FundRich's dividend history has no currency field, so callers must use the fund's NAV
    /// currency (from <see cref="IFundDataService.GetFundNavHistoryAsync"/>) instead.
    /// </param>
    public record DividendRecord(DateOnly ExDate, decimal AmountPerUnit, string Currency);

    /// <summary>
    /// Dividend/distribution history for stocks and funds, used to compute total (含息) return
    /// alongside the existing price-only return. Wrapped defensively like <see cref="FundDataService"/> —
    /// any failure degrades to an empty list rather than throwing, so a flaky third-party endpoint
    /// never breaks the portfolio summary itself.
    /// </summary>
    public interface IDividendService
    {
        /// <summary>
        /// Cash dividends for a stock via Yahoo Finance. For TW stocks, tries the listed (.TW)
        /// suffix first and falls back to OTC (.TWO) if that comes back empty/not-found.
        /// </summary>
        Task<List<DividendRecord>> GetStockDividendsAsync(string symbol, string market, CancellationToken ct = default);

        /// <summary>Cash distributions for a fund via FundRich. Records carry no currency (see <see cref="DividendRecord"/>).</summary>
        Task<List<DividendRecord>> GetFundDividendsAsync(string fundId, CancellationToken ct = default);
    }

    public static class DividendServiceExtensions
    {
        /// <summary>The dividends of a holding whose type <see cref="AssetValuationPolicy.PaysDividends"/>,
        /// from that type's source: a stock's from Yahoo, a fund's from FundRich.</summary>
        public static Task<List<DividendRecord>> GetDividendsAsync(
            this IDividendService dividends, AssetType type, string symbol, string market, CancellationToken ct) =>
            type switch
            {
                AssetType.STOCK => dividends.GetStockDividendsAsync(symbol, market, ct),
                AssetType.FUND => dividends.GetFundDividendsAsync(symbol, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "This asset type pays no dividends."),
            };
    }

    public class DividendService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<DividendService> logger) : IDividendService
    {
        private static readonly string YAHOO_CHART_URL = YahooChart.ChartBase;
        private const string FUND_DIVIDEND_URL = "https://apis.fundrich.com.tw/FRSDataCenter/GetFundDividendHistory";

        public async Task<List<DividendRecord>> GetStockDividendsAsync(string symbol, string market, CancellationToken ct = default)
        {
            var upperSymbol = (symbol ?? "").Trim().ToUpperInvariant();
            if (upperSymbol.Length == 0) return [];

            var upperMarket = (market ?? "").Trim().ToUpperInvariant();

            // Single-flight: a cold cache can see this symbol requested by several Portfolio
            // endpoints at once (same page load). A failed fetch answers null, which is not cached:
            // it says nothing about the stock's dividends, so the next call asks again instead of
            // counting none for a day.
            return await cache.GetOrCreateSingleFlightAsync($"dividends_stock_{upperMarket}_{upperSymbol}",
                CacheTtl.Portfolio.Dividend, () => FetchStockDividendsAsync(upperSymbol, upperMarket), ct) ?? [];
        }

        private async Task<List<DividendRecord>?> FetchStockDividendsAsync(string upperSymbol, string upperMarket)
        {
            // Shared by every caller waiting on it, so no one caller's token may stop it.
            var ct = CancellationToken.None;
            try
            {
                if (upperMarket == "US")
                    return await FetchYahooDividendsAsync(upperSymbol, ct) ?? [];

                // TW has no listed/OTC flag in our data model, so try the listed suffix first and
                // fall back to OTC — mirrors how a human would guess without extra lookups.
                var listed = await FetchYahooDividendsAsync($"{upperSymbol}.TW", ct);
                if (listed is { Count: > 0 }) return listed;
                return await FetchYahooDividendsAsync($"{upperSymbol}.TWO", ct) ?? listed ?? [];
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Dividend fetch failed for {Symbol}", upperSymbol);
                return null;
            }
        }

        /// <summary>
        /// Returns null when the symbol/suffix combination doesn't resolve on Yahoo at all (so the
        /// caller can try the next suffix), vs. an empty list when it resolved but simply has no
        /// dividend history (e.g. a growth stock that's never paid one). A failed fetch throws: it
        /// is neither, and must not be cached as either.
        /// </summary>
        private async Task<List<DividendRecord>?> FetchYahooDividendsAsync(string yahooSymbol, CancellationToken ct)
        {
            var client = httpClientFactory.CreateClient("ExternalReadOnly");
            // interval=1mo keeps the payload small — only the events block is needed, not daily bars.
            var url = $"{YAHOO_CHART_URL}/{yahooSymbol}?range=5y&interval=1mo&events=div";

            using var request = YahooChart.Request(url);
            using var response = await client.SendAsync(request, ct);
            // 404 is Yahoo's answer for a symbol it doesn't know. Anything else unsuccessful is a
            // failed fetch, not an answer about the symbol.
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var results = doc.RootElement.GetProperty("chart").GetProperty("result");
            if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                return null;

            var chartResult = results[0];
            var currency = chartResult.GetProperty("meta").TryGetProperty("currency", out var c)
                ? c.GetString() ?? "TWD"
                : "TWD";

            var records = new List<DividendRecord>();
            if (chartResult.TryGetProperty("events", out var events) &&
                events.TryGetProperty("dividends", out var dividends) &&
                dividends.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in dividends.EnumerateObject())
                {
                    if (!prop.Value.TryGetProperty("amount", out var amountEl) ||
                        !prop.Value.TryGetProperty("date", out var dateEl))
                        continue;

                    var amount = amountEl.GetDecimal();
                    if (amount <= 0) continue;

                    var exDate = DateOnly.FromDateTime(
                        DateTimeOffset.FromUnixTimeSeconds(dateEl.GetInt64()).UtcDateTime);
                    records.Add(new DividendRecord(exDate, amount, currency));
                }
            }
            return records;
        }

        public async Task<List<DividendRecord>> GetFundDividendsAsync(string fundId, CancellationToken ct = default)
        {
            fundId = (fundId ?? "").Trim();
            if (fundId.Length == 0) return [];

            // Single-flight: a cold cache can see this fundId requested by several Portfolio
            // endpoints at once (same page load). The shared fetch runs with its own
            // CancellationToken.None (see SingleFlight's doc) so one caller cancelling never cancels
            // it for the others still waiting. A failed fetch answers null, which is not cached.
            return await cache.GetOrCreateSingleFlightAsync($"dividends_fund_{fundId}", CacheTtl.Portfolio.Dividend, async () =>
            {
                var result = new List<DividendRecord>();
                try
                {
                    var client = httpClientFactory.CreateClient("ExternalReadOnly");
                    using var req = new HttpRequestMessage(HttpMethod.Post, FUND_DIVIDEND_URL);
                    AddFundRichHeaders(req);
                    var payload = JsonSerializer.Serialize(new { data = new { fundId } });
                    req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    using var resp = await client.SendAsync(req, CancellationToken.None);
                    resp.EnsureSuccessStatusCode();
                    var json = await resp.Content.ReadAsStringAsync(CancellationToken.None);

                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("tableRow", out var tableRow) &&
                        tableRow.TryGetProperty("history", out var history) &&
                        history.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in history.EnumerateArray())
                        {
                            var exDateStr = el.TryGetProperty("exDividendDate", out var ed) ? ed.GetString() : null;
                            var amountStr = el.TryGetProperty("perUnitPrice", out var p) ? p.GetString() : null;
                            if (string.IsNullOrWhiteSpace(exDateStr) || string.IsNullOrWhiteSpace(amountStr))
                                continue;
                            if (!DateTime.TryParseExact(exDateStr, "yyyy/MM/dd", CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var exDate))
                                continue;
                            if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ||
                                amount <= 0)
                                continue;

                            // Currency intentionally blank — see DividendRecord.Currency doc.
                            result.Add(new DividendRecord(DateOnly.FromDateTime(exDate), amount, ""));
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Not "no dividends": the next call asks again rather than count none for a day.
                    logger.LogWarning(ex, "Fund dividend fetch failed for {FundId}", fundId);
                    return null;
                }

                return result;
            }, ct) ?? [];
        }

        // FundRich rejects requests without a same-origin Referer/Origin (HTTP 406) — same
        // requirement as FundDataService, duplicated here since headers aren't shared between them.
        private static void AddFundRichHeaders(HttpRequestMessage req)
        {
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("Origin", "https://www.fundrich.com.tw");
            req.Headers.TryAddWithoutValidation("Referer", "https://www.fundrich.com.tw/");
        }
    }
}
