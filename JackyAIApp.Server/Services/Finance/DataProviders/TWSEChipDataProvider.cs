using JackyAIApp.Server.DTO.Finance;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Fetches chip (籌碼面) data from TWSE OpenAPI endpoints.
    /// Includes margin trading, foreign holdings, short selling, and director holdings.
    /// </summary>
    public class TWSEChipDataProvider : IMarketDataProvider, IChipDataProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TWSEChipDataProvider> _logger;

        private const string BASE_URL = "https://openapi.twse.com.tw/v1";
        private const string USER_AGENT = "stock-analysis/1.0";
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromHours(4);

        public DataProviderType Type => DataProviderType.Chip;

        public TWSEChipDataProvider(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            ILogger<TWSEChipDataProvider> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var marketData = new MarketData
            {
                StockCode = stockCode,
                Chips = new ChipData()
            };

            // Run all chip data fetches in parallel
            var tasks = new[]
            {
                FetchMarginDataAsync(stockCode, marketData.Chips, cancellationToken),
                FetchForeignHoldingAsync(stockCode, marketData.Chips, cancellationToken),
                FetchSBLDataAsync(stockCode, marketData.Chips, cancellationToken),
                FetchDirectorHoldingsAsync(stockCode, marketData.Chips, cancellationToken),
                FetchMajorShareholdersAsync(stockCode, marketData.Chips, cancellationToken),
                FetchDayTradingAsync(stockCode, marketData.Chips, cancellationToken)
            };

            await Task.WhenAll(tasks);

            return marketData;
        }

        /// <summary>
        /// Fetches margin trading data (融資融券) from MI_MARGN endpoint.
        /// </summary>
        private async Task FetchMarginDataAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                var data = await FetchTWSEArrayAsync("exchangeReport/MI_MARGN", ct);
                if (data == null) return;

                foreach (var item in data.Value.EnumerateArray())
                {
                    if (GetJsonString(item, "股票代號")?.Trim() == stockCode)
                    {
                        chipData.MarginBuyVolume = ParseLong(GetJsonString(item, "融資買進"));
                        chipData.MarginSellVolume = ParseLong(GetJsonString(item, "融資賣出"));
                        chipData.MarginCashRepayment = ParseLong(GetJsonString(item, "融資現金償還"));
                        chipData.MarginPreviousBalance = ParseLong(GetJsonString(item, "融資前日餘額"));
                        chipData.MarginBalance = ParseLong(GetJsonString(item, "融資今日餘額"));
                        chipData.MarginLimit = ParseLong(GetJsonString(item, "融資限額"));
                        chipData.ShortBuyVolume = ParseLong(GetJsonString(item, "融券買進"));
                        chipData.ShortSellVolume = ParseLong(GetJsonString(item, "融券賣出"));
                        chipData.ShortCashRepayment = ParseLong(GetJsonString(item, "融券現券償還"));
                        chipData.ShortPreviousBalance = ParseLong(GetJsonString(item, "融券前日餘額"));
                        chipData.ShortBalance = ParseLong(GetJsonString(item, "融券今日餘額"));
                        chipData.ShortLimit = ParseLong(GetJsonString(item, "融券限額"));
                        chipData.OffsetVolume = ParseLong(GetJsonString(item, "資券互抵"));

                        _logger.LogDebug("Fetched margin data for {stockCode}: margin={margin}, short={short}",
                            stockCode, chipData.MarginBalance, chipData.ShortBalance);
                        return;
                    }
                }

                _logger.LogDebug("No margin data found for {stockCode}", stockCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch margin data for {stockCode}", stockCode);
            }
        }

        /// <summary>
        /// Fetches foreign investor holding data from MI_QFIIS_sort_20 and MI_QFIIS_cat.
        /// </summary>
        private async Task FetchForeignHoldingAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                // Try top-20 list first (has per-stock data)
                var top20Data = await FetchTWSEArrayAsync("fund/MI_QFIIS_sort_20", ct);
                if (top20Data != null)
                {
                    foreach (var item in top20Data.Value.EnumerateArray())
                    {
                        if (GetJsonString(item, "Code")?.Trim() == stockCode)
                        {
                            chipData.ForeignHoldingShares = ParseLong(GetJsonString(item, "SharesHeld")?.Replace(",", ""));
                            chipData.ForeignHoldingPercentage = ParseDecimal(GetJsonString(item, "SharesHeldPer"));
                            chipData.ForeignAvailableShares = ParseLong(GetJsonString(item, "AvailableShare")?.Replace(",", ""));
                            chipData.ForeignUpperLimit = ParseDecimal(GetJsonString(item, "Upperlimit"));

                            _logger.LogDebug("Fetched foreign holding for {stockCode}: {pct}%",
                                stockCode, chipData.ForeignHoldingPercentage);
                            return;
                        }
                    }
                }

                _logger.LogDebug("Stock {stockCode} not in foreign top-20 list", stockCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch foreign holding data for {stockCode}", stockCode);
            }
        }

        /// <summary>
        /// Fetches securities borrowing and lending (SBL) data from TWT96U.
        /// </summary>
        private async Task FetchSBLDataAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                var data = await FetchTWSEArrayAsync("SBL/TWT96U", ct);
                if (data == null) return;

                foreach (var item in data.Value.EnumerateArray())
                {
                    if (GetJsonString(item, "TWSECode")?.Trim() == stockCode)
                    {
                        chipData.SBLAvailableVolume = ParseLong(
                            GetJsonString(item, "TWSEAvailableVolume")?.Replace(",", ""));

                        _logger.LogDebug("Fetched SBL data for {stockCode}: available={vol}",
                            stockCode, chipData.SBLAvailableVolume);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch SBL data for {stockCode}", stockCode);
            }
        }

        /// <summary>
        /// Fetches director/supervisor holdings from t187ap11_L.
        /// </summary>
        private async Task FetchDirectorHoldingsAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                var data = await FetchTWSEArrayAsync("opendata/t187ap11_L", ct);
                if (data == null) return;

                var directors = new List<DirectorHolding>();
                long totalPledged = 0;
                long totalHeld = 0;

                foreach (var item in data.Value.EnumerateArray())
                {
                    if (GetJsonString(item, "公司代號")?.Trim() == stockCode)
                    {
                        var held = ParseLong(GetJsonString(item, "目前持股"));
                        var pledged = ParseLong(GetJsonString(item, "設質股數"));
                        totalHeld += held;
                        totalPledged += pledged;

                        directors.Add(new DirectorHolding
                        {
                            Title = GetJsonString(item, "職稱") ?? "",
                            Name = GetJsonString(item, "姓名") ?? "",
                            CurrentShares = held,
                            PledgedShares = pledged,
                            PledgeRatio = GetJsonString(item, "設質股數佔持股比例") ?? "0.00%"
                        });
                    }
                }

                if (directors.Count > 0)
                {
                    chipData.DirectorHoldings = directors;
                    chipData.TotalDirectorShares = totalHeld;
                    chipData.TotalDirectorPledged = totalPledged;
                    chipData.DirectorPledgeRatio = totalHeld > 0
                        ? Math.Round((decimal)totalPledged / totalHeld * 100, 2)
                        : 0;

                    _logger.LogDebug("Fetched {count} director holdings for {stockCode}, pledge ratio={ratio}%",
                        directors.Count, stockCode, chipData.DirectorPledgeRatio);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch director holdings for {stockCode}", stockCode);
            }
        }

        /// <summary>
        /// Fetches major shareholders (>10%) from t187ap02_L.
        /// </summary>
        private async Task FetchMajorShareholdersAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                var data = await FetchTWSEArrayAsync("opendata/t187ap02_L", ct);
                if (data == null) return;

                var shareholders = new List<string>();

                foreach (var item in data.Value.EnumerateArray())
                {
                    if (GetJsonString(item, "公司代號")?.Trim() == stockCode)
                    {
                        var name = GetJsonString(item, "大股東名稱");
                        if (!string.IsNullOrEmpty(name))
                        {
                            shareholders.Add(name);
                        }
                    }
                }

                if (shareholders.Count > 0)
                {
                    chipData.MajorShareholders = shareholders;
                    _logger.LogDebug("Found {count} major shareholders for {stockCode}", shareholders.Count, stockCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch major shareholders for {stockCode}", stockCode);
            }
        }

        /// <summary>
        /// Fetches day trading suspension info from TWTB4U.
        /// </summary>
        private async Task FetchDayTradingAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                var data = await FetchTWSEArrayAsync("exchangeReport/TWTB4U", ct);
                if (data == null) return;

                foreach (var item in data.Value.EnumerateArray())
                {
                    if (GetJsonString(item, "Code")?.Trim() == stockCode)
                    {
                        var suspension = GetJsonString(item, "Suspension")?.Trim();
                        chipData.DayTradingSuspended = !string.IsNullOrEmpty(suspension);
                        _logger.LogDebug("Day trading status for {stockCode}: suspended={suspended}",
                            stockCode, chipData.DayTradingSuspended);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch day trading data for {stockCode}", stockCode);
            }
        }

        #region Helper Methods

        /// <summary>
        /// Fetches a TWSE OpenAPI endpoint that returns a JSON array, with caching.
        /// </summary>
        private async Task<JsonElement?> FetchTWSEArrayAsync(string path, CancellationToken ct)
        {
            var cacheKey = $"TWSE_{path}_{DateTime.Today:yyyyMMdd}";

            if (_cache.TryGetValue(cacheKey, out JsonElement? cached) && cached != null)
            {
                return cached;
            }

            var url = $"{BASE_URL}/{path}";
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", USER_AGENT);

            var response = await httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TWSE API {path} returned {status}", path, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(content) || content == "[]")
            {
                _logger.LogDebug("TWSE API {path} returned empty data", path);
                return null;
            }

            var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("TWSE API {path} did not return an array", path);
                return null;
            }

            var element = doc.RootElement.Clone();
            _cache.Set(cacheKey, element, CACHE_DURATION);

            _logger.LogDebug("Fetched and cached TWSE API {path} ({count} items)",
                path, element.GetArrayLength());

            return element;
        }

        private static string? GetJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                return prop.GetString();
            }
            return null;
        }

        private static long ParseLong(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "--" || value == "-")
                return 0;
            return long.TryParse(value.Replace(",", ""), out var result) ? result : 0;
        }

        private static decimal ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "--" || value == "-")
                return 0;
            return decimal.TryParse(value.Replace(",", "").Replace("%", ""), out var result) ? result : 0;
        }

        #endregion
    }
}
