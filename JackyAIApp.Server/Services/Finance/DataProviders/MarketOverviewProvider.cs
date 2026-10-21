using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Builds the market dashboard from Yahoo Finance's free v8 chart endpoint. One uniform quote
    /// call serves indices, FX, crypto, gold and yields. Each ticker is fetched independently and
    /// degrades gracefully — a failed source is simply omitted rather than failing the whole board.
    ///
    /// <para>The 加權指數 is the exception: it comes from TWSE itself, never Yahoo. Yahoo's
    /// <c>^TWII</c> <c>chartPreviousClose</c> has been seen a trading day behind (on 2026-10-20 it
    /// still held 10/16's close, so the day's −529.15 showed as −818.37). The live reading is MIS
    /// <c>t00</c> (z now, y the previous close), shared with the 回落煞車 estimate through
    /// <see cref="IMisTaiexQuoteSource"/>; when MIS has none (before the open, or down), the latest
    /// day of OpenAPI FMTQIK, whose <c>Change</c> is the exchange's own.</para>
    /// </summary>
    public class MarketOverviewProvider : IMarketOverviewProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMisTaiexQuoteSource _taiexQuotes;
        private readonly ILogger<MarketOverviewProvider> _logger;

        private static readonly string CHART_URL = YahooChart.ChartBase;

        internal const string FMTQIK_URL = "https://openapi.twse.com.tw/v1/exchangeReport/FMTQIK";

        /// <summary>The ticker served from TWSE instead of Yahoo; kept in <see cref="Tickers"/> for its place.</summary>
        internal const string TaiexSymbol = "^TWII";

        // (group, Yahoo symbol, display name) — output keeps this order within each group.
        private static readonly (string Group, string Symbol, string Name)[] Tickers =
        {
            ("US", "^GSPC", "S&P 500"),
            ("US", "^IXIC", "NASDAQ"),
            ("US", "^VIX", "VIX"),
            ("TW", TaiexSymbol, "加權指數"),
            ("TW", "0050.TW", "元大台灣50"),
            ("FX", "TWD=X", "USD/TWD"),
            ("FX", "JPYTWD=X", "JPY/TWD"),
            ("Other", "BTC-USD", "BTC"),
            ("Other", "GC=F", "黃金"),
            ("Other", "^TNX", "美債10年殖利率"),
        };

        public MarketOverviewProvider(
            IHttpClientFactory httpClientFactory,
            IMisTaiexQuoteSource taiexQuotes,
            ILogger<MarketOverviewProvider> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _taiexQuotes = taiexQuotes ?? throw new ArgumentNullException(nameof(taiexQuotes));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<MarketOverviewResponse> FetchAsync(CancellationToken cancellationToken = default)
        {
            var tasks = Tickers.Select(t => t.Symbol == TaiexSymbol
                ? FetchTaiexSafeAsync(t.Group, t.Symbol, t.Name, cancellationToken)
                : FetchQuoteSafeAsync(t.Group, t.Symbol, t.Name, cancellationToken));
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
                return BuildQuote(group, symbol, name, price, change, prev);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch market quote for {symbol}", symbol);
                return null;
            }
        }

        private async Task<MarketQuote?> FetchTaiexSafeAsync(string group, string symbol, string name, CancellationToken ct)
        {
            try
            {
                var snapshot = await _taiexQuotes.GetAsync(Common.CacheTtl.Portfolio.DrawdownBrakeEstimateQuote, ct);
                if (snapshot.Quote is { } mis)
                    return BuildQuote(group, symbol, name, mis.Last, mis.Last - mis.PreviousClose, mis.PreviousClose);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read the MIS TAIEX quote; falling back to FMTQIK");
            }

            try
            {
                var client = _httpClientFactory.CreateClient("ExternalReadOnly");
                using var response = await client.GetAsync(FMTQIK_URL, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("FMTQIK returned {status}", response.StatusCode);
                    return null;
                }

                var rows = JsonSerializer.Deserialize<List<JsonElement>>(await response.Content.ReadAsStringAsync(ct));
                // Date is 民國 YYYMMDD, so the largest is the latest trading day.
                var latest = rows?
                    .Where(r => ParseTwseDecimal(r, "TAIEX") is > 0 && ParseTwseDecimal(r, "Change").HasValue)
                    .MaxBy(r => r.TryGetProperty("Date", out var d) ? d.GetString() : null);
                if (latest is not { } day)
                    return null;

                var close = ParseTwseDecimal(day, "TAIEX")!.Value;
                var change = ParseTwseDecimal(day, "Change")!.Value;
                return BuildQuote(group, symbol, name, close, change, close - change);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch the TAIEX from FMTQIK");
                return null;
            }
        }

        private static MarketQuote BuildQuote(string group, string symbol, string name, decimal? price, decimal? change, decimal? prev)
        {
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

        private static decimal? ParseTwseDecimal(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var prop) || prop.ValueKind != JsonValueKind.String)
                return null;
            var s = prop.GetString()?.Trim().Replace(",", "");
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        private static decimal? GetDecimal(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var prop) || prop.ValueKind != JsonValueKind.Number)
                return null;
            return prop.TryGetDecimal(out var d) ? d : (decimal)prop.GetDouble();
        }
    }
}
