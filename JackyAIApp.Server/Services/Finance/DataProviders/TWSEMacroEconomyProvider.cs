using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Fetches macro economy data from multiple free sources:
    /// - TWSE FMTQIK: Market index (TAIEX), through <see cref="ITwseOpenApi"/>
    /// - TWSE MI_INDEX: Sector indices, through <see cref="ITwseOpenApi"/>
    /// - TWSE MI_MARGN: Margin trading summary, through <see cref="ITwseOpenApi"/>
    /// - open.er-api.com: Exchange rates against TWD (mid-market, JSON)
    /// - CBC: Bank interest rates (CSV)
    /// </summary>
    public class TWSEMacroEconomyProvider : IMacroEconomyProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TWSEMacroEconomyProvider> _logger;
        private readonly ITwseOpenApi _twse;

        private const string FX_URL = "https://open.er-api.com/v6/latest/USD";
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
            ILogger<TWSEMacroEconomyProvider> logger,
            ITwseOpenApi twse)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _logger = logger;
            _twse = twse;
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
                var data = await _twse.GetMarketDaysAsync(ct);
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
                var data = await _twse.GetMarketIndicesAsync(ct);
                if (data == null) return null;

                return data
                    .Where(r => KeySectors.Contains(GetStr(r, "指數")))
                    .Select(r => new SectorIndex
                    {
                        Date = GetStr(r, "日期"),
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
                var data = await _twse.GetMarginTradingAsync(ct);
                if (data == null) return null;

                var summary = new MarginSummary();
                foreach (var r in data)
                {
                    summary.MarginBuyTotal += GetLong(r, "融資買進");
                    summary.MarginSellTotal += GetLong(r, "融資賣出");
                    summary.MarginBalanceTotal += GetLong(r, "融資今日餘額");
                    summary.MarginPrevBalanceTotal += GetLong(r, "融資前日餘額");
                    summary.ShortSellTotal += GetLong(r, "融券賣出");
                    summary.ShortBuyTotal += GetLong(r, "融券買進");
                    summary.ShortBalanceTotal += GetLong(r, "融券今日餘額");
                    summary.ShortPrevBalanceTotal += GetLong(r, "融券前日餘額");
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
                // An empty list (none of the key currencies parsed) is returned but not cached.
                return await _cache.GetOrCreateSingleFlightAsync("macro_FX", TimeSpan.FromHours(CACHE_HOURS),
                    FetchExchangeRatesUncachedAsync, ct, shouldCache: rates => rates.Count > 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch exchange rates");
                return null;
            }
        }

        private async Task<List<ExchangeRate>> FetchExchangeRatesUncachedAsync()
        {
            // Shared by every caller waiting on it, so no one caller's token may stop it.
            var ct = CancellationToken.None;
            var client = _httpClientFactory.CreateClient("ExternalReadOnly");
            // Not Bank of Taiwan's flcsv: it answers non-browser clients with an anti-bot
            // "Challenge Validation" page, so its spot buy/sell never arrived. open.er-api.com (what
            // Portfolio converts USD at) quotes every currency per USD, one mid-market rate a day.
            using var doc = JsonDocument.Parse(await client.GetStringAsync(FX_URL, ct));

            var rates = new List<ExchangeRate>();
            if (!doc.RootElement.TryGetProperty("rates", out var perUsd) || !TryReadRate(perUsd, "TWD", out var twdPerUsd))
                return rates;

            foreach (var (currency, name) in KeyCurrencies)
            {
                if (!TryReadRate(perUsd, currency, out var currencyPerUsd)) continue;
                rates.Add(new ExchangeRate
                {
                    Currency = currency,
                    DisplayName = name,
                    Rate = Math.Round(twdPerUsd / currencyPerUsd, 4),
                });
            }

            return rates;

            static bool TryReadRate(JsonElement rates, string currency, out decimal rate)
            {
                rate = 0;
                return rates.ValueKind == JsonValueKind.Object && rates.TryGetProperty(currency, out var value)
                    && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out rate) && rate > 0;
            }
        }

        private async Task<BankRate?> FetchBankRates(CancellationToken ct)
        {
            try
            {
                return await _cache.GetOrCreateSingleFlightAsync("macro_BankRate", TimeSpan.FromHours(CACHE_HOURS),
                    FetchBankRatesUncachedAsync, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch bank rates");
                return null;
            }
        }

        private async Task<BankRate?> FetchBankRatesUncachedAsync()
        {
            // Shared by every caller waiting on it, so no one caller's token may stop it.
            var ct = CancellationToken.None;
            var client = _httpClientFactory.CreateClient("ExternalReadOnly");
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

            return result;
        }

        // === Helpers ===

        private static string GetStr(TwseRow row, string field) => row.Value(field)?.Trim() ?? "";

        private static decimal GetDec(TwseRow row, string field)
        {
            var s = row.Value(field)?.Trim().Replace(",", "") ?? "";
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
        }

        private static long GetLong(TwseRow row, string field)
        {
            var s = row.Value(field)?.Trim().Replace(",", "") ?? "";
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
