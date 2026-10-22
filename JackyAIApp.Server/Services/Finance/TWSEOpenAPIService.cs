using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Service for interacting with Taiwan Stock Exchange Open API.
    /// </summary>
    public class TWSEOpenAPIService : ITWSEOpenAPIService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<TWSEOpenAPIService> _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly ITwseOpenApi _twse;
        private const string BaseUrl = "https://openapi.twse.com.tw/v1";
        private const string UserAgent = "stock-analysis/1.0";
        private const int CacheHours = 4;

        public TWSEOpenAPIService(
            IHttpClientFactory httpClientFactory,
            ILogger<TWSEOpenAPIService> logger,
            IMemoryCache memoryCache,
            ITwseOpenApi twse)
        {
            _httpClient = httpClientFactory?.CreateClient("ExternalReadOnly") ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _twse = twse;
        }

        /// <summary>
        /// Retrieves comprehensive stock data from TWSE Open API endpoints organized by analysis timeframe.
        /// </summary>
        public async Task<string> GetStockDataAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            try
            {
                // Launch every section in parallel. Each endpoint is a market-wide array that is
                // cached by date and shared across all stocks, so the first call of the day pays
                // the download and the rest are cache hits.
                var dailyTradingTask = GetStockDailyTradingAsync(stockCode, cancellationToken);
                var marketIndexTask = GetMarketIndexInfoAsync(cancellationToken);
                var monthlyAvgTask = GetStockMonthlyAverageAsync(stockCode, cancellationToken);
                var monthlyRevenueTask = GetMonthlyRevenueAsync(stockCode, cancellationToken);
                var incomeStatementTask = GetIncomeStatementAsync(stockCode, cancellationToken);
                var balanceSheetTask = GetBalanceSheetAsync(stockCode, cancellationToken);
                var dividendTask = GetCompanyDividendAsync(stockCode, cancellationToken);
                var valuationTask = GetStockValuationRatiosAsync(stockCode, cancellationToken);
                var companyProfileTask = GetCompanyProfileAsync(stockCode, cancellationToken);

                await Task.WhenAll(
                    dailyTradingTask, marketIndexTask, monthlyAvgTask, monthlyRevenueTask,
                    incomeStatementTask, balanceSheetTask, dividendTask, valuationTask, companyProfileTask);

                var stockData = new StringBuilder();
                stockData.AppendLine($"=== Comprehensive Stock Analysis for {stockCode} ===");
                stockData.AppendLine();

                // Short-Term Analysis (1-3 months) - Technical & Chip Analysis
                stockData.AppendLine("=== SHORT-TERM ANALYSIS (1-3 months) ===");
                stockData.AppendLine("Data Sources: Technical indicators and market sentiment");
                stockData.AppendLine();
                AppendSection(stockData, "Daily Trading Data (Technical)", await dailyTradingTask);
                AppendSection(stockData, "Market Index & Institutional Activity (Chip)", await marketIndexTask);

                // Medium-Term Analysis (3-12 months) - Technical & Fundamental
                stockData.AppendLine("=== MEDIUM-TERM ANALYSIS (3-12 months) ===");
                stockData.AppendLine("Data Sources: Monthly trends and fundamental performance");
                stockData.AppendLine();
                AppendSection(stockData, "Monthly Average Prices (Technical)", await monthlyAvgTask);
                AppendSection(stockData, "Monthly Revenue (Fundamental)", await monthlyRevenueTask);
                AppendSection(stockData, "Income Statement (Fundamental)", await incomeStatementTask);
                AppendSection(stockData, "Balance Sheet (Fundamental)", await balanceSheetTask);

                // Long-Term Analysis (1-3 years) - Valuation & Fundamentals
                stockData.AppendLine("=== LONG-TERM ANALYSIS (1-3 years) ===");
                stockData.AppendLine("Data Sources: Valuation metrics and dividend policy");
                stockData.AppendLine();
                AppendSection(stockData, "Dividend Distribution (Fundamental)", await dividendTask);
                AppendSection(stockData, "Valuation Ratios (P/E, P/B, Dividend Yield)", await valuationTask);

                // Company Profile for context
                stockData.AppendLine("=== COMPANY CONTEXT ===");
                AppendSection(stockData, "Company Profile", await companyProfileTask);

                var result = stockData.ToString();

                if (string.IsNullOrWhiteSpace(result) || result.Length < 200)
                {
                    _logger.LogWarning("Insufficient stock data retrieved from TWSE APIs for {stockCode}", stockCode);
                    return $"Limited data available for {stockCode}. Stock code provided for basic analysis.";
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve stock data from TWSE APIs for {stockCode}", stockCode);
                return $"Failed to retrieve stock data for {stockCode}. Error: {ex.Message}";
            }
        }

        private static void AppendSection(StringBuilder sb, string title, string content)
        {
            if (string.IsNullOrEmpty(content)) return;
            sb.AppendLine($"--- {title} ---");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        /// <summary>
        /// Gets stock daily trading data from TWSE API.
        /// </summary>
        public async Task<string> GetStockDailyTradingAsync(string stockCode, CancellationToken cancellationToken = default) =>
            Describe(await _twse.GetDailyQuotesAsync(cancellationToken), stockCode);

        /// <summary>
        /// Gets company profile from TWSE API.
        /// </summary>
        public async Task<string> GetCompanyProfileAsync(string stockCode, CancellationToken cancellationToken = default) =>
            Describe(await _twse.GetCompanyProfilesAsync(cancellationToken), stockCode);

        /// <summary>
        /// Gets stock monthly average from TWSE API.
        /// </summary>
        public async Task<string> GetStockMonthlyAverageAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var content = await FetchRawWithCacheAsync($"{BaseUrl}/exchangeReport/STOCK_DAY_AVG_ALL", "STOCK_DAY_AVG_ALL", cancellationToken);
            return string.IsNullOrEmpty(content) ? string.Empty : FilterStockDataByCode(content, stockCode);
        }

        /// <summary>
        /// Gets stock valuation ratios from TWSE API.
        /// </summary>
        public async Task<string> GetStockValuationRatiosAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var content = await FetchRawWithCacheAsync($"{BaseUrl}/exchangeReport/BWIBBU_ALL", "BWIBBU_ALL", cancellationToken);
            return string.IsNullOrEmpty(content) ? string.Empty : FilterStockDataByCode(content, stockCode);
        }

        /// <summary>
        /// Gets company dividend information from TWSE API.
        /// </summary>
        public async Task<string> GetCompanyDividendAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var content = await FetchRawWithCacheAsync($"{BaseUrl}/opendata/t187ap45_L", "t187ap45_L", cancellationToken);
            return string.IsNullOrEmpty(content) ? string.Empty : FilterStockDataByCode(content, stockCode);
        }

        /// <summary>
        /// Gets market index information from TWSE API.
        /// </summary>
        public async Task<string> GetMarketIndexInfoAsync(CancellationToken cancellationToken = default)
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            var requestUrl = $"{BaseUrl}/exchangeReport/MI_INDEX?response=json&date={today}&type=ALLBUT0999";
            var content = await FetchRawWithCacheAsync(requestUrl, "MI_INDEX", cancellationToken);
            // Truncate to avoid overwhelming the analysis.
            return content.Length > 1000 ? content.Substring(0, 1000) + "..." : content;
        }

        /// <summary>
        /// Gets monthly revenue data from TWSE API.
        /// </summary>
        public async Task<string> GetMonthlyRevenueAsync(string stockCode, CancellationToken cancellationToken = default) =>
            Describe(await _twse.GetMonthlyRevenuesAsync(cancellationToken), stockCode);

        /// <summary>The stock's row of a dataset as "field: value" lines; empty when the dataset could
        /// not be read (its section is then left out).</summary>
        private static string Describe<T>(IReadOnlyList<T>? rows, string stockCode) where T : TwseRow
        {
            if (rows == null) return string.Empty;
            var row = rows.FirstOrDefault(r => CodeOf(r) == stockCode);
            if (row == null) return $"No data found for stock code: {stockCode}";

            var result = new StringBuilder();
            foreach (var (name, value) in row.Fields)
                result.AppendLine($"{name}: {value}");
            return result.ToString();
        }

        private static string CodeOf(TwseRow row) => row switch
        {
            TwseDailyQuote q => q.Code,
            TwseCompanyProfile p => p.Code,
            TwseMonthlyRevenue r => r.Code,
            _ => "",
        };

        /// <summary>
        /// Gets comprehensive income statement from TWSE API.
        /// </summary>
        public async Task<string> GetIncomeStatementAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var content = await FetchRawWithCacheAsync($"{BaseUrl}/opendata/t187ap06_L_ci", "t187ap06_L_ci", cancellationToken);
            return string.IsNullOrEmpty(content) ? string.Empty : FilterStockDataByCode(content, stockCode);
        }

        /// <summary>
        /// Gets balance sheet from TWSE API.
        /// </summary>
        public async Task<string> GetBalanceSheetAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var content = await FetchRawWithCacheAsync($"{BaseUrl}/opendata/t187ap07_L_ci", "t187ap07_L_ci", cancellationToken);
            return string.IsNullOrEmpty(content) ? string.Empty : FilterStockDataByCode(content, stockCode);
        }

        /// <summary>
        /// Fetches a TWSE endpoint's raw JSON, cached by date. The arrays are market-wide and
        /// identical across stocks, so caching here removes repeated full-market downloads.
        /// User-Agent is set per-request (not on the shared HttpClient) so concurrent calls are
        /// thread-safe — mutating DefaultRequestHeaders while parallel requests are in flight races.
        /// </summary>
        private Task<string> FetchRawWithCacheAsync(string url, string cacheKey, CancellationToken ct) =>
            // A blank answer (failed, or an empty body) is returned but not cached, so the next call
            // tries again.
            _memoryCache.GetOrCreateSingleFlightAsync($"TWSERaw_{cacheKey}_{DateTime.Today:yyyyMMdd}",
                TimeSpan.FromHours(CacheHours), () => FetchRawAsync(url, cacheKey), ct,
                shouldCache: content => !string.IsNullOrWhiteSpace(content));

        private async Task<string> FetchRawAsync(string url, string cacheKey)
        {
            // Shared by every caller waiting on it, so no one caller's token may stop it.
            var ct = CancellationToken.None;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                using var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("TWSE API {cacheKey} returned {status}", cacheKey, response.StatusCode);
                    return string.Empty;
                }

                return await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch TWSE API {cacheKey}", cacheKey);
                return string.Empty;
            }
        }

        /// <summary>
        /// Filters JSON response data to find entries matching the stock code.
        /// </summary>
        private string FilterStockDataByCode(string jsonContent, string stockCode)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(jsonContent);

                // Handle direct array response
                if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return FilterArrayData(jsonDoc.RootElement, stockCode);
                }

                // Handle object with data property
                if (jsonDoc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                {
                    return FilterArrayData(dataArray, stockCode);
                }

                // If no filtering possible, return original content truncated
                return jsonContent.Length > 1000 ? jsonContent.Substring(0, 1000) + "..." : jsonContent;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to filter stock data for code {stockCode}", stockCode);
                return jsonContent;
            }
        }

        /// <summary>
        /// Filters array data to find the first matching stock code entry.
        /// </summary>
        private string FilterArrayData(JsonElement dataArray, string stockCode)
        {
            foreach (var item in dataArray.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    // Check for exact match on stock code fields
                    if (item.TryGetProperty("公司代號", out var companyCode))
                    {
                        if (companyCode.GetString() == stockCode)
                        {
                            return FormatJsonAsKeyValuePairs(item);
                        }
                    }

                    // Also check for "Code" field (some APIs use English)
                    if (item.TryGetProperty("Code", out var codeField))
                    {
                        if (codeField.GetString() == stockCode)
                        {
                            return FormatJsonAsKeyValuePairs(item);
                        }
                    }

                    // Check for stock symbol/number fields
                    if (item.TryGetProperty("證券代號", out var stockSymbol))
                    {
                        if (stockSymbol.GetString() == stockCode)
                        {
                            return FormatJsonAsKeyValuePairs(item);
                        }
                    }
                }
            }

            // No exact match found
            return $"No data found for stock code: {stockCode}";
        }

        /// <summary>
        /// Formats JSON object as multiline key-value pairs similar to Python MCP server.
        /// </summary>
        private string FormatJsonAsKeyValuePairs(JsonElement jsonElement)
        {
            var result = new StringBuilder();

            foreach (var property in jsonElement.EnumerateObject())
            {
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? "",
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "null",
                    _ => property.Value.GetRawText()
                };

                result.AppendLine($"{property.Name}: {value}");
            }

            return result.ToString();
        }
    }
}
