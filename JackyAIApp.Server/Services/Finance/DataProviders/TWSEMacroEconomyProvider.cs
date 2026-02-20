using JackyAIApp.Server.DTO.Finance;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Fetches macro economy data from multiple free sources:
    /// - TWSE FMTQIK: Market index (TAIEX)
    /// - TWSE MI_INDEX: Sector indices
    /// - TWSE MI_MARGN: Margin trading summary
    /// - Bank of Taiwan: Exchange rates (CSV)
    /// - CBC: Bank interest rates (CSV)
    /// </summary>
    public class TWSEMacroEconomyProvider : IMacroEconomyProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TWSEMacroEconomyProvider> _logger;

        private const string FMTQIK_URL = "https://openapi.twse.com.tw/v1/exchangeReport/FMTQIK";
        private const string MI_INDEX_URL = "https://openapi.twse.com.tw/v1/exchangeReport/MI_INDEX";
        private const string MI_MARGN_URL = "https://openapi.twse.com.tw/v1/exchangeReport/MI_MARGN";
        private const string BOT_FX_URL = "https://rate.bot.com.tw/xrt/flcsv/0/day";
        private const string CBC_RATE_URL = "https://www.cbc.gov.tw/public/data/OpenData/A13Rate.csv";
        private const int CACHE_HOURS = 2;

        // Key sector indices to display
        private static readonly HashSet<string> KeySectors = new()
        {
            "發行量加權股價指數",
            "半導體類指數",
            "電子工業類指數",
            "金融保險類指數",
            "航運類指數",
            "臺灣50指數",
            "生技醫療類指數",
            "通信網路類指數",
        };

        // Key currencies to display
        private static readonly Dictionary<string, string> KeyCurrencies = new()
        {
            ["USD"] = "美元",
            ["JPY"] = "日圓",
            ["EUR"] = "歐元",
            ["CNY"] = "人民幣",
            ["GBP"] = "英鎊",
        };

        public TWSEMacroEconomyProvider(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            ILogger<TWSEMacroEconomyProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _logger = logger;
        }

        public async Task<MacroEconomyResponse> FetchAsync(CancellationToken ct = default)
        {
            var response = new MacroEconomyResponse();

            // Fetch all sources in parallel
            var indexTask = FetchMarketIndex(ct);
            var sectorTask = FetchSectorIndices(ct);
            var marginTask = FetchMarginSummary(ct);
            var fxTask = FetchExchangeRates(ct);
            var rateTask = FetchBankRates(ct);

            await Task.WhenAll(indexTask, sectorTask, marginTask, fxTask, rateTask);

            response.MarketIndex = await indexTask ?? new List<MarketIndexDay>();
            response.SectorIndices = await sectorTask ?? new List<SectorIndex>();
            response.Margin = await marginTask;
            response.ExchangeRates = await fxTask ?? new List<ExchangeRate>();
            response.BankRate = await rateTask;

            return response;
        }

        private async Task<List<MarketIndexDay>?> FetchMarketIndex(CancellationToken ct)
        {
            try
            {
                var data = await FetchJsonCached<List<JsonElement>>(FMTQIK_URL, "macro_FMTQIK", ct);
                if (data == null) return null;

                return data.Select(r => new MarketIndexDay
                {
                    Date = GetStr(r, "Date"),
                    TAIEX = GetDec(r, "TAIEX"),
                    Change = GetDec(r, "Change"),
                    TradeVolume = GetLong(r, "TradeVolume"),
                    TradeValue = GetLong(r, "TradeValue"),
                    Transaction = GetLong(r, "Transaction"),
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch market index");
                return null;
            }
        }

        private async Task<List<SectorIndex>?> FetchSectorIndices(CancellationToken ct)
        {
            try
            {
                var data = await FetchJsonCached<List<JsonElement>>(MI_INDEX_URL, "macro_MI_INDEX", ct);
                if (data == null) return null;

                return data
                    .Where(r => KeySectors.Contains(GetStr(r, "指數")))
                    .Select(r => new SectorIndex
                    {
                        Name = GetStr(r, "指數"),
                        CloseIndex = GetDec(r, "收盤指數"),
                        Direction = GetStr(r, "漲跌"),
                        ChangePoints = GetDec(r, "漲跌點數"),
                        ChangePercent = GetDec(r, "漲跌百分比"),
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch sector indices");
                return null;
            }
        }

        private async Task<MarginSummary?> FetchMarginSummary(CancellationToken ct)
        {
            try
            {
                var data = await FetchJsonCached<List<JsonElement>>(MI_MARGN_URL, "macro_MI_MARGN", ct);
                if (data == null) return null;

                var summary = new MarginSummary();
                foreach (var r in data)
                {
                    summary.MarginBuyTotal += GetLong(r, "融資買進");
                    summary.MarginSellTotal += GetLong(r, "融資賣出");
                    summary.MarginBalanceTotal += GetLong(r, "融資今日餘額");
                    summary.ShortSellTotal += GetLong(r, "融券賣出");
                    summary.ShortBuyTotal += GetLong(r, "融券買進");
                    summary.ShortBalanceTotal += GetLong(r, "融券今日餘額");
                }
                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch margin summary");
                return null;
            }
        }

        private async Task<List<ExchangeRate>?> FetchExchangeRates(CancellationToken ct)
        {
            try
            {
                if (_cache.TryGetValue("macro_FX", out List<ExchangeRate>? cached))
                    return cached;

                var client = _httpClientFactory.CreateClient();
                var csv = await client.GetStringAsync(BOT_FX_URL, ct);
                var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                var rates = new List<ExchangeRate>();
                foreach (var line in lines.Skip(1)) // skip header
                {
                    var cols = line.Split(',');
                    if (cols.Length < 13) continue;

                    var currency = cols[0].Trim();
                    if (!KeyCurrencies.ContainsKey(currency)) continue;

                    rates.Add(new ExchangeRate
                    {
                        Currency = currency,
                        DisplayName = KeyCurrencies[currency],
                        BuyRate = ParseDec(cols[3]),   // 即期買入
                        SellRate = ParseDec(cols[13]),  // 即期賣出
                    });
                }

                if (rates.Count > 0)
                    _cache.Set("macro_FX", rates, TimeSpan.FromHours(CACHE_HOURS));

                return rates;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch exchange rates");
                return null;
            }
        }

        private async Task<BankRate?> FetchBankRates(CancellationToken ct)
        {
            try
            {
                if (_cache.TryGetValue("macro_BankRate", out BankRate? cached))
                    return cached;

                var client = _httpClientFactory.CreateClient();
                var csv = await client.GetStringAsync(CBC_RATE_URL, ct);
                var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                // Find the latest entry for a major bank (臺灣銀行 or 第一銀行)
                BankRate? result = null;
                foreach (var line in lines.Skip(1).Reverse())
                {
                    var cols = line.Split(',');
                    if (cols.Length < 31) continue;

                    var bank = cols[0].Trim();
                    if (bank != "臺灣銀行" && bank != "第一銀行") continue;

                    result = new BankRate
                    {
                        BankName = bank,
                        Period = cols[1].Trim(),
                        OneYearFixed = ParseDec(cols[14]),      // 定存一年期固定
                        OneYearFloating = ParseDec(cols[15]),   // 定存一年期機動
                        BaseLendingRate = ParseDec(cols[30]),   // 基準利率
                    };
                    break;
                }

                if (result != null)
                    _cache.Set("macro_BankRate", result, TimeSpan.FromHours(CACHE_HOURS));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch bank rates");
                return null;
            }
        }

        // === Helpers ===

        private async Task<T?> FetchJsonCached<T>(string url, string cacheKey, CancellationToken ct) where T : class
        {
            if (_cache.TryGetValue(cacheKey, out T? cached))
                return cached;

            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<T>(json);

            if (result != null)
                _cache.Set(cacheKey, result, TimeSpan.FromHours(CACHE_HOURS));

            return result;
        }

        private static string GetStr(JsonElement el, string prop)
        {
            return el.TryGetProperty(prop, out var v) ? v.GetString()?.Trim() ?? "" : "";
        }

        private static decimal GetDec(JsonElement el, string prop)
        {
            if (!el.TryGetProperty(prop, out var v)) return 0;
            var s = v.GetString()?.Trim().Replace(",", "") ?? "";
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
        }

        private static long GetLong(JsonElement el, string prop)
        {
            if (!el.TryGetProperty(prop, out var v)) return 0;
            var s = v.GetString()?.Trim().Replace(",", "") ?? "";
            return long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? l : 0;
        }

        private static decimal? ParseDec(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().Replace(",", "");
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
        }
    }
}
