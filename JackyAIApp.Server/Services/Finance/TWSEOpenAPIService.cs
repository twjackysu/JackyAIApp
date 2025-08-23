using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
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
        private readonly IOpenAIService _openAIService;
        private readonly IMemoryCache _memoryCache;
        private const string BaseUrl = "https://openapi.twse.com.tw/v1";
        private const string UserAgent = "stock-analysis/1.0";
        private const string CompanyListCacheKey = "twse_company_list";

        public TWSEOpenAPIService(
            IHttpClientFactory httpClientFactory, 
            ILogger<TWSEOpenAPIService> logger,
            IOpenAIService openAIService,
            IMemoryCache memoryCache)
        {
            _httpClient = httpClientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
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

        /// <summary>
        /// Resolves user input to a valid stock code using AI and company database.
        /// </summary>
        public async Task<string> ResolveStockCodeAsync(string userInput, CancellationToken cancellationToken = default)
        {
            try
            {
                // First, check if input is already a valid stock code (numeric)
                if (IsValidStockCode(userInput))
                {
                    return userInput;
                }

                // Get company list and try to find exact or partial matches
                var companies = await GetCompanyListAsync(cancellationToken);
                
                // Try exact company name match first with LLM assistance
                var exactMatch = await FindExactCompanyMatchAsync(companies, userInput, cancellationToken);
                if (!string.IsNullOrEmpty(exactMatch))
                {
                    return exactMatch;
                }

                // Use AI to resolve ambiguous or partial company names
                var aiResolvedCode = await ResolveWithAIAsync(userInput, companies, cancellationToken);
                if (!string.IsNullOrEmpty(aiResolvedCode))
                {
                    return aiResolvedCode;
                }

                _logger.LogWarning("Could not resolve user input '{userInput}' to a stock code", userInput);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving stock code for input '{userInput}'", userInput);
                return userInput; // Return original input as fallback
            }
        }

        /// <summary>
        /// Checks if the input is a valid stock code (4-digit number).
        /// </summary>
        private bool IsValidStockCode(string input)
        {
            return !string.IsNullOrEmpty(input) && 
                   input.Length == 4 && 
                   input.All(char.IsDigit);
        }

        /// <summary>
        /// Gets the cached company list or fetches it from TWSE API.
        /// </summary>
        private async Task<List<CompanyInfo>> GetCompanyListAsync(CancellationToken cancellationToken)
        {
            if (_memoryCache.TryGetValue(CompanyListCacheKey, out List<CompanyInfo>? cachedCompanies))
            {
                return cachedCompanies ?? new List<CompanyInfo>();
            }

            try
            {
                var apiUrl = $"{BaseUrl}/opendata/t187ap03_L";
                SetUserAgent();
                
                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    var companies = ParseCompanyList(content);
                    
                    // Cache for 24 hours
                    _memoryCache.Set(CompanyListCacheKey, companies, TimeSpan.FromHours(24));
                    return companies;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch company list from TWSE API");
            }

            return new List<CompanyInfo>();
        }

        /// <summary>
        /// Parses the company list from TWSE API response.
        /// </summary>
        private List<CompanyInfo> ParseCompanyList(string jsonContent)
        {
            var companies = new List<CompanyInfo>();
            
            try
            {
                var jsonDoc = JsonDocument.Parse(jsonContent);
                
                if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in jsonDoc.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            var companyInfo = ExtractCompanyInfo(item);
                            if (companyInfo != null)
                            {
                                companies.Add(companyInfo);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse company list");
            }

            return companies;
        }

        /// <summary>
        /// Extracts company information from JSON element.
        /// </summary>
        private CompanyInfo? ExtractCompanyInfo(JsonElement item)
        {
            try
            {
                string? code = null;
                string? name = null;

                if (item.TryGetProperty("公司代號", out var codeProperty))
                {
                    code = codeProperty.GetString();
                }

                if (item.TryGetProperty("公司名稱", out var nameProperty))
                {
                    name = nameProperty.GetString();
                }

                if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(name))
                {
                    return new CompanyInfo { Code = code, Name = name };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract company info from JSON element");
            }

            return null;
        }

        /// <summary>
        /// Uses LLM to find the best matching company from the full company list.
        /// </summary>
        private async Task<string> FindExactCompanyMatchAsync(List<CompanyInfo> companies, string userInput, CancellationToken cancellationToken)
        {
            try
            {
                // First try simple exact match for performance
                var exactMatch = companies.FirstOrDefault(c => 
                    string.Equals(c.Name, userInput, StringComparison.OrdinalIgnoreCase));

                if (exactMatch != null)
                {
                    return exactMatch.Code;
                }

                // If no exact match, use LLM to find the best match
                var companyList = companies
                    .Take(100) // Limit to avoid token overflow
                    .Select(c => $"{c.Code}:{c.Name}")
                    .ToList();

                var systemPrompt = @"你是台灣證券交易所的股票代號查詢助手。
給定用戶輸入和公司列表，請找出最匹配的公司代號。

規則：
1. 只返回4位數字的股票代號 (例如: 2330)
2. 如果找不到匹配，返回 'NONE'
3. 優先考慮知名公司和常見簡稱
4. 支援部分匹配和簡稱

例子：
- 用戶輸入 '台積電' 或 '台積' → 找到 '台灣積體電路製造股份有限公司' → 返回 '2330'
- 用戶輸入 '鴻海' → 找到 '鴻海精密工業股份有限公司' → 返回 '2317'
- 用戶輸入 '聯發科' → 找到對應公司 → 返回代號";

                var userPrompt = $@"用戶輸入: '{userInput}'

公司列表:
{string.Join("\n", companyList)}

請返回最匹配的股票代號:";

                var completionRequest = new ChatCompletionCreateRequest
                {
                    Messages = new List<ChatMessage>
                    {
                        ChatMessage.FromSystem(systemPrompt),
                        ChatMessage.FromUser(userPrompt)
                    },
                    Model = NewModels.GPT_5_NANO,
                    Temperature = 0.1f,
                    MaxTokens = 10
                };

                var response = await _openAIService.ChatCompletion.CreateCompletion(completionRequest);
                
                if (response.Successful)
                {
                    var result = response.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";
                    
                    // Validate that the result is a 4-digit stock code
                    if (IsValidStockCode(result))
                    {
                        _logger.LogInformation("LLM resolved '{input}' to stock code '{code}'", userInput, result);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM company matching failed for input '{userInput}'", userInput);
            }

            return string.Empty;
        }

        /// <summary>
        /// Uses AI to resolve ambiguous company names to stock codes.
        /// </summary>
        private async Task<string> ResolveWithAIAsync(string userInput, List<CompanyInfo> companies, CancellationToken cancellationToken)
        {
            try
            {
                // Create a focused list of potential matches
                var potentialMatches = companies
                    .Where(c => c.Name.Contains(userInput, StringComparison.OrdinalIgnoreCase) ||
                               ContainsKeywords(c.Name, userInput))
                    .Take(20) // Limit to top 20 matches to avoid overwhelming AI
                    .Select(c => $"{c.Code}: {c.Name}")
                    .ToList();

                if (potentialMatches.Count == 0)
                {
                    return string.Empty;
                }

                var systemPrompt = @"You are a stock code resolver for Taiwan Stock Exchange (TWSE). 
Given a user input and a list of potential company matches, return ONLY the 4-digit stock code that best matches the user's intent.

Rules:
1. Return ONLY the 4-digit stock code (e.g., '2330')
2. If no good match exists, return 'NONE'
3. Consider common abbreviations and variations
4. Prioritize well-known companies for ambiguous inputs

Examples:
User: '台積電' → '2330'
User: 'TSMC' → '2330' 
User: '台積' → '2330'
User: '鴻海' → '2317'
User: 'undefined company' → 'NONE'";

                var userPrompt = $@"User input: '{userInput}'

Potential matches:
{string.Join("\n", potentialMatches)}

Return the best matching stock code:";

                var completionRequest = new ChatCompletionCreateRequest
                {
                    Messages = new List<ChatMessage>
                    {
                        ChatMessage.FromSystem(systemPrompt),
                        ChatMessage.FromUser(userPrompt)
                    },
                    Model = NewModels.GPT_5_NANO,
                    Temperature = 0.1f,
                    MaxTokens = 50
                };

                var response = await _openAIService.ChatCompletion.CreateCompletion(completionRequest);
                
                if (response.Successful)
                {
                    var result = response.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";
                    
                    // Validate that the result is a 4-digit stock code
                    if (IsValidStockCode(result))
                    {
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI resolution failed for input '{userInput}'", userInput);
            }

            return string.Empty;
        }

        /// <summary>
        /// Checks if company name contains keywords from user input.
        /// </summary>
        private bool ContainsKeywords(string companyName, string userInput)
        {
            var keywords = userInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return keywords.Any(keyword => 
                companyName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Company information model.
        /// </summary>
        private class CompanyInfo
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }
    }
}