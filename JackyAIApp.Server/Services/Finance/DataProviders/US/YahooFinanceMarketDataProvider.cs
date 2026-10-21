using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance.DataProviders.US
{
    /// <summary>
    /// Fetches US stock historical price data from Yahoo Finance v8 Chart API.
    /// Free, no API key required. Needs User-Agent header.
    /// </summary>
    public class YahooFinanceMarketDataProvider : IMarketDataProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<YahooFinanceMarketDataProvider> _logger;

        private static readonly string CHART_URL = YahooChart.ChartBase;
        private const int CACHE_HOURS = 1;

        public DataProviderType Type => DataProviderType.HistoricalPrice;

        public YahooFinanceMarketDataProvider(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            ILogger<YahooFinanceMarketDataProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _logger = logger;
        }

        // Yahoo's chart API returns the whole requested range in one HTTP call regardless of
        // length (unlike TWStockLib, which pays a real per-month cost) — no benefit to a separate
        // shallow fetch here, so just reuse the same (already cached) full fetch.
        public Task<MarketData> FetchLatestAsync(string stockCode, CancellationToken cancellationToken = default) =>
            FetchAsync(stockCode, cancellationToken);

        public async Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var symbol = stockCode.ToUpperInvariant();
            // A failed fetch throws and is not cached; the next call tries again.
            return await _cache.GetOrCreateSingleFlightAsync($"Yahoo_Chart_{symbol}", TimeSpan.FromHours(CACHE_HOURS), async () =>
            {
                // Shared by every caller waiting on it, so no one caller's token may stop it.
                var url = $"{CHART_URL}/{symbol}?range=6mo&interval=1d&includePrePost=false";
                var result = await FetchUrlAsync(url, symbol, CancellationToken.None);
                _logger.LogInformation("Fetched {count} price records for {symbol} from Yahoo Finance",
                    result.HistoricalPrices.Count, symbol);
                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// Fetches a specific date range via Yahoo's <c>period1</c>/<c>period2</c> query params
        /// instead of <see cref="FetchAsync"/>'s fixed <c>range=6mo</c> window. Needed by
        /// <c>PredictionEvaluationService</c> to look up a price near an arbitrary past date — a
        /// due long-horizon prediction's target date can be up to ~3 years in the past, well
        /// outside <see cref="FetchAsync"/>'s 6-month window. Deliberately not cached (unlike
        /// <see cref="FetchAsync"/>): callers ask for a narrow, case-specific window rather than
        /// "the last 6 months" that many callers would otherwise share.
        /// </summary>
        public virtual async Task<MarketData> FetchRangeAsync(
            string stockCode, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            var symbol = stockCode.ToUpperInvariant();
            var period1 = new DateTimeOffset(DateTime.SpecifyKind(startDate, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var period2 = new DateTimeOffset(DateTime.SpecifyKind(endDate, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var url = $"{CHART_URL}/{symbol}?period1={period1}&period2={period2}&interval=1d&includePrePost=false";
            return await FetchUrlAsync(url, symbol, cancellationToken);
        }

        private async Task<MarketData> FetchUrlAsync(string url, string symbol, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient("ExternalReadOnly");
            using var request = YahooChart.Request(url);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseChartResponse(json, symbol);
        }

        private static MarketData ParseChartResponse(string json, string symbol)
        {
            var doc = JsonDocument.Parse(json);

            var chartResult = doc.RootElement
                .GetProperty("chart")
                .GetProperty("result")[0];

            var meta = chartResult.GetProperty("meta");
            var timestamps = chartResult.GetProperty("timestamp");
            var quote = chartResult.GetProperty("indicators")
                .GetProperty("quote")[0];

            var opens = quote.GetProperty("open");
            var highs = quote.GetProperty("high");
            var lows = quote.GetProperty("low");
            var closes = quote.GetProperty("close");
            var volumes = quote.GetProperty("volume");

            var prices = new List<DailyPrice>();
            for (int i = 0; i < timestamps.GetArrayLength(); i++)
            {
                var ts = timestamps[i].GetInt64();
                var date = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;

                // Skip entries with null values
                if (closes[i].ValueKind == JsonValueKind.Null) continue;

                prices.Add(new DailyPrice
                {
                    Date = date,
                    Open = GetDecimalSafe(opens[i]),
                    High = GetDecimalSafe(highs[i]),
                    Low = GetDecimalSafe(lows[i]),
                    Close = GetDecimalSafe(closes[i]),
                    Volume = volumes[i].ValueKind != JsonValueKind.Null ? volumes[i].GetInt64() : 0
                });
            }

            var companyName = meta.TryGetProperty("longName", out var ln) ? ln.GetString() ?? symbol
                            : meta.TryGetProperty("shortName", out var sn) ? sn.GetString() ?? symbol
                            : symbol;

            return new MarketData
            {
                StockCode = symbol,
                CompanyName = companyName,
                HistoricalPrices = prices
            };
        }

        private static decimal GetDecimalSafe(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Null) return 0;
            return el.TryGetDecimal(out var d) ? d : (decimal)el.GetDouble();
        }
    }
}
