using JackyAIApp.Server.DTO;
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
        private const string BaseUrl = "https://openapi.twse.com.tw/v1";
        private const string UserAgent = "stock-analysis/1.0";

        public TWSEOpenAPIService(
            IHttpClientFactory httpClientFactory,
            ILogger<TWSEOpenAPIService> logger,
            IMemoryCache memoryCache)
        {
            _httpClient = httpClientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        }

        /// <summary>
        /// Retrieves comprehensive stock data from TWSE Open API endpoints organized by analysis timeframe.
        /// </summary>
        public async Task<string> GetStockDataAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            try
            {
                var stockData = new StringBuilder();
                stockData.AppendLine($"=== Comprehensive Stock Analysis for {stockCode} ===");
                stockData.AppendLine();

                // Short-Term Analysis (1-3 months) - Technical & Chip Analysis
                stockData.AppendLine("=== SHORT-TERM ANALYSIS (1-3 months) ===");
                stockData.AppendLine("Data Sources: Technical indicators and market sentiment");
                stockData.AppendLine();

                var shortTermTasks = new[]
                {
                    GetStockDailyTradingAsync(stockCode, cancellationToken),
                    GetMarketIndexInfoAsync(cancellationToken)
                };

                var shortTermResults = await Task.WhenAll(shortTermTasks);
                var shortTermTypes = new[] { "Daily Trading Data (Technical)", "Market Index & Institutional Activity (Chip)" };

                for (int i = 0; i < shortTermResults.Length; i++)
                {
                    if (!string.IsNullOrEmpty(shortTermResults[i]))
                    {
                        stockData.AppendLine($"--- {shortTermTypes[i]} ---");
                        stockData.AppendLine(shortTermResults[i]);
                        stockData.AppendLine();
                    }
                }

                // Medium-Term Analysis (3-12 months) - Technical & Fundamental
                stockData.AppendLine("=== MEDIUM-TERM ANALYSIS (3-12 months) ===");
                stockData.AppendLine("Data Sources: Monthly trends and fundamental performance");
                stockData.AppendLine();

                var mediumTermTasks = new[]
                {
                    GetStockMonthlyAverageAsync(stockCode, cancellationToken),
                    GetMonthlyRevenueAsync(stockCode, cancellationToken),
                    GetIncomeStatementAsync(stockCode, cancellationToken),
                    GetBalanceSheetAsync(stockCode, cancellationToken)
                };

                var mediumTermResults = await Task.WhenAll(mediumTermTasks);
                var mediumTermTypes = new[] 
                { 
                    "Monthly Average Prices (Technical)", 
                    "Monthly Revenue (Fundamental)", 
                    "Income Statement (Fundamental)", 
                    "Balance Sheet (Fundamental)" 
                };

                for (int i = 0; i < mediumTermResults.Length; i++)
                {
                    if (!string.IsNullOrEmpty(mediumTermResults[i]))
                    {
                        stockData.AppendLine($"--- {mediumTermTypes[i]} ---");
                        stockData.AppendLine(mediumTermResults[i]);
                        stockData.AppendLine();
                    }
                }

                // Long-Term Analysis (1-3 years) - Valuation & Fundamentals
                stockData.AppendLine("=== LONG-TERM ANALYSIS (1-3 years) ===");
                stockData.AppendLine("Data Sources: Valuation metrics and dividend policy");
                stockData.AppendLine();

                var longTermTasks = new[]
                {
                    GetCompanyDividendAsync(stockCode, cancellationToken),
                    GetStockValuationRatiosAsync(stockCode, cancellationToken)
                };

                var longTermResults = await Task.WhenAll(longTermTasks);
                var longTermTypes = new[] 
                { 
                    "Dividend Distribution (Fundamental)", 
                    "Valuation Ratios (P/E, P/B, Dividend Yield)" 
                };

                for (int i = 0; i < longTermResults.Length; i++)
                {
                    if (!string.IsNullOrEmpty(longTermResults[i]))
                    {
                        stockData.AppendLine($"--- {longTermTypes[i]} ---");
                        stockData.AppendLine(longTermResults[i]);
                        stockData.AppendLine();
                    }
                }

                // Company Profile for context
                stockData.AppendLine("=== COMPANY CONTEXT ===");
                var companyProfile = await GetCompanyProfileAsync(stockCode, cancellationToken);
                if (!string.IsNullOrEmpty(companyProfile))
                {
                    stockData.AppendLine("--- Company Profile ---");
                    stockData.AppendLine(companyProfile);
                    stockData.AppendLine();
                }

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

        /// <summary>
        /// Gets stock daily trading data from TWSE API.
        /// </summary>
        public async Task<string> GetStockDailyTradingAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            try
            {
                var apiUrl = $"{BaseUrl}/exchangeReport/STOCK_DAY_ALL";
                
                SetUserAgent();
                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    return FilterStockDataByCode(content, stockCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get daily trading data for stock {stockCode}", stockCode);
            }
            return string.Empty;
        }

        /// <summary>
        /// Gets company profile from TWSE API.
        /// </summary>
        public async Task<string> GetCompanyProfileAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            try
            {
                var apiUrl = $"{BaseUrl}/opendata/t187ap03_L";
                
                SetUserAgent();
                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    return FilterStockDataByCode(content, stockCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get company profile for stock {stockCode}", stockCode);
            }
            return string.Empty;
        }

        /// <summary>
        /// Gets stock monthly average from TWSE API.
        /// </summary>
        public async Task<string> GetStockMonthlyAverageAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            try
            {
                var apiUrl = $"{BaseUrl}/exchangeReport/STOCK_DAY_AVG_ALL";
                
                SetUserAgent();
                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    return FilterStockDataByCode(content, stockCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get monthly average for stock {stockCode}", stockCode);
            }
            return string.Empty;
        }

        /// <summary>
        /// Gets stock valuation ratios from TWSE API.
        /// </summary>
        public async Task<string> GetStockValuationRatiosAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            try
            {
                var apiUrl = $"{BaseUrl}/exchangeReport/BWIBBU_ALL";
                
                SetUserAgent();
                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    return FilterStockDataByCode(content, stockCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get valuation ratios for stock {stockCode}", stockCode);
            }
            return string.Empty;
        }

        /// <summary>
        /// Gets company dividend information from TWSE API.
        /// </summary>
        public async Task<string> GetCompanyDividendAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            try
            {
                var apiUrl = $"{BaseUrl}/opendata/t187ap45_L";
                
                SetUserAgent();
                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    return FilterStockDataByCode(content, stockCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get dividend info for stock {stockCode}", stockCode);
            }
            return string.Empty;
        }

        /// <summary>
        /// Gets market index information from TWSE API.
        /// </summary>
        public async Task<string> GetMarketIndexInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var apiUrl = $"{BaseUrl}/exchangeReport/MI_INDEX";
                var today = DateTime.Now.ToString("yyyyMMdd");
                var requestUrl = $"{apiUrl}?response=json&date={today}&type=ALLBUT0999";
                
                SetUserAgent();
                using var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    // Return first 1000 characters to avoid overwhelming the analysis
                    return content.Length > 1000 ? content.Substring(0, 1000) + "..." : content;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get market index info");
            }
            return string.Empty;
        }

        /// <summary>
        /// Gets monthly revenue data from TWSE API.
        /// </summary>
        public async Task<string> GetMonthlyRevenueAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            try
            {
                var apiUrl = $"{BaseUrl}/opendata/t187ap05_L";
                
                SetUserAgent();
                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    return FilterStockDataByCode(content, stockCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get monthly revenue for stock {stockCode}", stockCode);
            }
            return string.Empty;
        }

        /// <summary>
        /// Gets comprehensive income statement from TWSE API.
        /// </summary>
        public async Task<string> GetIncomeStatementAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            try
            {
                var apiUrl = $"{BaseUrl}/opendata/t187ap06_L_ci";
                
                SetUserAgent();
                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    return FilterStockDataByCode(content, stockCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get income statement for stock {stockCode}", stockCode);
            }
            return string.Empty;
        }

        /// <summary>
        /// Gets balance sheet from TWSE API.
        /// </summary>
        public async Task<string> GetBalanceSheetAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            try
            {
                var apiUrl = $"{BaseUrl}/opendata/t187ap07_L_ci";
                
                SetUserAgent();
                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    return FilterStockDataByCode(content, stockCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get balance sheet for stock {stockCode}", stockCode);
            }
            return string.Empty;
        }

        /// <summary>
        /// Sets the User-Agent header for HTTP requests.
        /// </summary>
        private void SetUserAgent()
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
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