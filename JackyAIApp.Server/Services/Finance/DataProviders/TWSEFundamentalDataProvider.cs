using JackyAIApp.Server.DTO.Finance;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Fetches fundamental data from TWSE OpenAPI endpoints:
    /// - BWIBBU_d: P/E ratio, P/B ratio, Dividend Yield
    /// - t187ap05_L: Monthly revenue, YoY, MoM
    /// - t187ap14_L: EPS (latest reported quarter)
    /// </summary>
    public class TWSEFundamentalDataProvider : IFundamentalDataProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TWSEFundamentalDataProvider> _logger;

        private const string BWIBBU_URL = "https://openapi.twse.com.tw/v1/exchangeReport/BWIBBU_d";
        private const string REVENUE_URL = "https://openapi.twse.com.tw/v1/opendata/t187ap05_L";
        private const string EPS_URL = "https://openapi.twse.com.tw/v1/opendata/t187ap14_L";
        private const int CACHE_HOURS = 4;

        public TWSEFundamentalDataProvider(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            ILogger<TWSEFundamentalDataProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _logger = logger;
        }

        public async Task<FundamentalData?> FetchAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var result = new FundamentalData();
            var hasData = false;

            // Fetch all sources in parallel
            var peTask = FetchValuationData(stockCode, cancellationToken);
            var revenueTask = FetchRevenueData(stockCode, cancellationToken);
            var epsTask = FetchEPSData(stockCode, cancellationToken);

            await Task.WhenAll(peTask, revenueTask, epsTask);

            // Merge P/E, P/B, Dividend Yield
            var valuation = await peTask;
            if (valuation != null)
            {
                result.PERatio = valuation.PERatio;
                result.PBRatio = valuation.PBRatio;
                result.DividendYield = valuation.DividendYield;
                result.FiscalYearQuarter = valuation.FiscalYearQuarter;
                hasData = true;
            }

            // Merge Revenue
            var revenue = await revenueTask;
            if (revenue != null)
            {
                result.MonthlyRevenue = revenue.MonthlyRevenue;
                result.RevenueYoY = revenue.RevenueYoY;
                result.RevenueMoM = revenue.RevenueMoM;
                result.RevenueMonth = revenue.RevenueMonth;
                hasData = true;
            }

            // Merge EPS
            var eps = await epsTask;
            if (eps != null)
            {
                result.EPS = eps.EPS;
                result.OperatingIncome = eps.OperatingIncome;
                result.NetIncome = eps.NetIncome;
                hasData = true;
            }

            return hasData ? result : null;
        }

        private async Task<FundamentalData?> FetchValuationData(string stockCode, CancellationToken ct)
        {
            try
            {
                var data = await FetchJsonArrayCached<JsonElement>(BWIBBU_URL, "BWIBBU_d", ct);
                if (data == null) return null;
                var stock = data.FirstOrDefault(r =>
                    r.TryGetProperty("Code", out var code) && code.GetString() == stockCode);

                if (stock.ValueKind == JsonValueKind.Undefined) return null;

                return new FundamentalData
                {
                    PERatio = ParseDecimal(stock, "PEratio"),
                    PBRatio = ParseDecimal(stock, "PBratio"),
                    DividendYield = ParseDecimal(stock, "DividendYield"),
                    FiscalYearQuarter = stock.TryGetProperty("FiscalYearQuarter", out var fq) ? fq.GetString() : null
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch valuation data for {stockCode}", stockCode);
                return null;
            }
        }

        private async Task<FundamentalData?> FetchRevenueData(string stockCode, CancellationToken ct)
        {
            try
            {
                var data = await FetchJsonArrayCached<JsonElement>(REVENUE_URL, "Revenue_t187ap05", ct);
                if (data == null) return null;
                var stock = data.FirstOrDefault(r =>
                    r.TryGetProperty("公司代號", out var code) && code.GetString()?.Trim() == stockCode);

                if (stock.ValueKind == JsonValueKind.Undefined) return null;

                return new FundamentalData
                {
                    MonthlyRevenue = ParseDecimalFromChinese(stock, "營業收入-當月營收"),
                    RevenueYoY = ParseDecimalFromChinese(stock, "營業收入-去年同月增減(%)"),
                    RevenueMoM = ParseDecimalFromChinese(stock, "營業收入-上月比較增減(%)"),
                    RevenueMonth = stock.TryGetProperty("資料年月", out var rm) ? rm.GetString() : null
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch revenue data for {stockCode}", stockCode);
                return null;
            }
        }

        private async Task<FundamentalData?> FetchEPSData(string stockCode, CancellationToken ct)
        {
            try
            {
                var data = await FetchJsonArrayCached<JsonElement>(EPS_URL, "EPS_t187ap14", ct);
                if (data == null) return null;
                var stock = data.FirstOrDefault(r =>
                    r.TryGetProperty("公司代號", out var code) && code.GetString()?.Trim() == stockCode);

                if (stock.ValueKind == JsonValueKind.Undefined) return null;

                return new FundamentalData
                {
                    EPS = ParseDecimalFromChinese(stock, "基本每股盈餘(元)"),
                    OperatingIncome = ParseDecimalFromChinese(stock, "營業利益"),
                    NetIncome = ParseDecimalFromChinese(stock, "稅後淨利")
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch EPS data for {stockCode}", stockCode);
                return null;
            }
        }

        private async Task<List<T>?> FetchJsonArrayCached<T>(string url, string cacheKey, CancellationToken ct)
        {
            if (_cache.TryGetValue(cacheKey, out List<T>? cached))
                return cached;

            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<List<T>>(json);

            if (result != null)
                _cache.Set(cacheKey, result, TimeSpan.FromHours(CACHE_HOURS));

            return result;
        }

        private static decimal? ParseDecimal(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var prop)) return null;
            var str = prop.GetString()?.Trim();
            if (string.IsNullOrEmpty(str) || str == "-" || str == "N/A") return null;
            return decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : null;
        }

        private static decimal? ParseDecimalFromChinese(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var prop)) return null;
            var str = prop.GetString()?.Trim();
            if (string.IsNullOrEmpty(str) || str == "-" || str == "N/A") return null;
            // Remove commas and whitespace
            str = str.Replace(",", "").Replace(" ", "");
            return decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : null;
        }
    }
}
