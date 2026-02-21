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

        private const string CHART_URL = "https://query1.finance.yahoo.com/v8/finance/chart";
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

        public async Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"Yahoo_Chart_{stockCode.ToUpperInvariant()}";
            if (_cache.TryGetValue(cacheKey, out MarketData? cached) && cached != null)
                return cached;

            var symbol = stockCode.ToUpperInvariant();
            var url = $"{CHART_URL}/{symbol}?range=6mo&interval=1d&includePrePost=false";

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

            var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
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

            var result = new MarketData
            {
                StockCode = symbol,
                CompanyName = companyName,
                HistoricalPrices = prices
            };

            _cache.Set(cacheKey, result, TimeSpan.FromHours(CACHE_HOURS));
            _logger.LogInformation("Fetched {count} price records for {symbol} from Yahoo Finance",
                prices.Count, symbol);

            return result;
        }

        private static decimal GetDecimalSafe(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Null) return 0;
            return el.TryGetDecimal(out var d) ? d : (decimal)el.GetDouble();
        }
    }
}
