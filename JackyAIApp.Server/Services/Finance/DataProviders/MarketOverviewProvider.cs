using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Builds the market dashboard from Yahoo Finance's free v8 chart endpoint. One uniform quote
    /// call serves indices, FX, crypto, gold and yields. Each ticker is fetched independently and
    /// degrades gracefully — a failed source is simply omitted rather than failing the whole board.
    /// </summary>
    public class MarketOverviewProvider : IMarketOverviewProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MarketOverviewProvider> _logger;

        private static readonly string CHART_URL = YahooChart.ChartBase;

        // (group, Yahoo symbol, display name) — output keeps this order within each group.
        private static readonly (string Group, string Symbol, string Name)[] Tickers =
        {
            ("US", "^GSPC", "S&P 500"),
            ("US", "^IXIC", "NASDAQ"),
            ("US", "^VIX", "VIX"),
            ("TW", "^TWII", "加權指數"),
            ("TW", "0050.TW", "元大台灣50"),
            ("FX", "TWD=X", "USD/TWD"),
            ("FX", "JPYTWD=X", "JPY/TWD"),
            ("Other", "BTC-USD", "BTC"),
            ("Other", "GC=F", "黃金"),
            ("Other", "^TNX", "美債10年殖利率"),
        };

        public MarketOverviewProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<MarketOverviewProvider> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<MarketOverviewResponse> FetchAsync(CancellationToken cancellationToken = default)
        {
            var tasks = Tickers.Select(t => FetchQuoteSafeAsync(t.Group, t.Symbol, t.Name, cancellationToken));
            var quotes = await Task.WhenAll(tasks);

            return new MarketOverviewResponse
            {
                Quotes = quotes.Where(q => q != null).Select(q => q!).ToList(),
                GeneratedAt = DateTime.UtcNow,
            };
        }

        private async Task<MarketQuote?> FetchQuoteSafeAsync(string group, string symbol, string name, CancellationToken ct)
        {
            try
            {
                var url = $"{CHART_URL}/{Uri.EscapeDataString(symbol)}?range=1d&interval=1d";

                var client = _httpClientFactory.CreateClient("ExternalReadOnly");
                using var request = YahooChart.Request(url);

                using var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Yahoo quote {symbol} returned {status}", symbol, response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                var result = doc.RootElement.GetProperty("chart").GetProperty("result");
                if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
                    return null;

                var meta = result[0].GetProperty("meta");
                var price = GetDecimal(meta, "regularMarketPrice");
                var prev = GetDecimal(meta, "chartPreviousClose") ?? GetDecimal(meta, "previousClose");

                decimal? change = price.HasValue && prev.HasValue ? price - prev : null;
                decimal? pct = change.HasValue && prev is > 0
                    ? Math.Round(change.Value / prev.Value * 100, 2)
                    : null;

                return new MarketQuote
                {
                    Symbol = symbol,
                    Name = name,
                    Group = group,
                    Price = price,
                    Change = change.HasValue ? Math.Round(change.Value, 2) : null,
                    ChangePercent = pct,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch market quote for {symbol}", symbol);
                return null;
            }
        }

        private static decimal? GetDecimal(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var prop) || prop.ValueKind != JsonValueKind.Number)
                return null;
            return prop.TryGetDecimal(out var d) ? d : (decimal)prop.GetDouble();
        }
    }
}
