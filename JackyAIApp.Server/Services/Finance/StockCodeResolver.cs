using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.AiUsage;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using ReasoningEffortType = Betalgo.Ranul.OpenAI.Contracts.Enums.ReasoningEffort;

namespace JackyAIApp.Server.Services.Finance
{
    public class StockCodeResolver(
        IHttpClientFactory httpClientFactory,
        ILogger<StockCodeResolver> logger,
        IOpenAIService openAIService,
        IMemoryCache memoryCache,
        IAiUsageLogger usageLogger,
        IUserService userService) : IStockCodeResolver
    {
        private readonly HttpClient _httpClient = httpClientFactory.CreateClient("ExternalReadOnly");
        private readonly ILogger<StockCodeResolver> _logger = logger;
        private readonly IOpenAIService _openAIService = openAIService;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly IAiUsageLogger _usageLogger = usageLogger;
        private readonly IUserService _userService = userService;

        private const string BaseUrl = "https://openapi.twse.com.tw/v1";
        private const string UserAgent = "stock-analysis/1.0";
        private const string CompanyListCacheKey = "twse_company_list";

        /// <summary>The answer is a stock code or "NONE"; the rest is headroom for low-effort reasoning.</summary>
        private const int ResolveMaxCompletionTokens = 1024;

        public async Task<string> ResolveStockCodeAsync(string userInput, CancellationToken cancellationToken = default)
        {
            try
            {
                // Input that is already shaped like a TW code (incl. 5-6 digit ETFs such as 00878 / 00631L)
                // is the answer: the company list only holds 4-digit listed companies, so sending it to the
                // LLM can only fail (or cost a call to fail).
                if (TwStockCode.TryNormalize(userInput, out var code))
                    return code;

                var companies = await GetCompanyListAsync(cancellationToken);

                var exactMatch = await FindExactCompanyMatchAsync(companies, userInput, cancellationToken);
                if (!string.IsNullOrEmpty(exactMatch))
                    return exactMatch;

                var aiResolvedCode = await ResolveWithAIAsync(userInput, companies, cancellationToken);
                if (!string.IsNullOrEmpty(aiResolvedCode))
                    return aiResolvedCode;

                _logger.LogWarning("Could not resolve user input '{userInput}' to a stock code", userInput);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving stock code for input '{userInput}'", userInput);
                return userInput;
            }
        }

        /// <summary>
        /// Validates what the LLM answered, not what the user typed (that is <see cref="TwStockCode.TryNormalize"/>).
        /// Stays strictly 4 digits: the company list holds only 4-digit listed companies, so a 5-6 digit
        /// answer would be a hallucination.
        /// </summary>
        private static bool IsFourDigitCompanyCode(string input) =>
            !string.IsNullOrEmpty(input) && input.Length == 4 && input.All(char.IsDigit);

        private Task LogUsageAsync(Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels.ChatCompletionCreateResponse response)
        {
            var usage = response.Usage;
            if (usage == null) return Task.CompletedTask;
            return _usageLogger.LogChatAsync(
                "finance.stockcode_resolver",
                NewModels.GPT_6_LUNA,
                usage.PromptTokens,
                usage.CompletionTokens ?? 0,
                response.Successful,
                _userService.GetUserId());
        }

        /// <summary>
        /// A request OpenAI rejects outright (e.g. HTTP 400 on an unsupported parameter) usually carries no
        /// <c>Usage</c>, so <see cref="LogUsageAsync"/> writes nothing and the caller only sees "not found".
        /// This leaves the reason in the log, matching <c>OpenAIPromptService</c>.
        /// </summary>
        private void LogCompletionFailure(Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels.ChatCompletionCreateResponse response)
        {
            var errorMsg = response.Error?.Message ?? "OpenAI request failed";
            _logger.LogError("OpenAI stock-code resolution failed: {Error}", errorMsg);
        }

        private async Task<List<CompanyInfo>> GetCompanyListAsync(CancellationToken cancellationToken)
        {
            if (_memoryCache.TryGetValue(CompanyListCacheKey, out List<CompanyInfo>? cached))
                return cached ?? [];

            try
            {
                SetUserAgent();
                using var response = await _httpClient.GetAsync($"{BaseUrl}/opendata/t187ap03_L", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    var companies = ParseCompanyList(content);
                    _memoryCache.Set(CompanyListCacheKey, companies, TimeSpan.FromHours(24));
                    return companies;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch company list from TWSE API");
            }

            return [];
        }

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
                            var info = ExtractCompanyInfo(item);
                            if (info != null)
                                companies.Add(info);
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

        private CompanyInfo? ExtractCompanyInfo(JsonElement item)
        {
            try
            {
                string? code = null, name = null;
                if (item.TryGetProperty("公司代號", out var cp)) code = cp.GetString();
                if (item.TryGetProperty("公司名稱", out var np)) name = np.GetString();
                if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(name))
                    return new CompanyInfo { Code = code, Name = name };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract company info from JSON element");
            }
            return null;
        }

        private async Task<string> FindExactCompanyMatchAsync(List<CompanyInfo> companies, string userInput, CancellationToken cancellationToken)
        {
            try
            {
                var exact = companies.FirstOrDefault(c => string.Equals(c.Name, userInput, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact.Code;

                var companyList = companies
                    .Take(100)
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

                var userPrompt = $"用戶輸入: '{userInput}'\n\n公司列表:\n{string.Join("\n", companyList)}\n\n請返回最匹配的股票代號:";

                var completionRequest = new ChatCompletionCreateRequest
                {
                    Messages = [ChatMessage.FromSystem(systemPrompt), ChatMessage.FromUser(userPrompt)],
                    Model = NewModels.GPT_6_LUNA,
                    // gpt-6-luna is a reasoning model: it rejects max_tokens, and its reasoning tokens
                    // count against the cap, so a tight cap meant for the answer alone returns empty.
                    ReasoningEffort = ReasoningEffortType.Low,
                    MaxCompletionTokens = ResolveMaxCompletionTokens
                };

                var response = await _openAIService.ChatCompletion.CreateCompletion(completionRequest);
                await LogUsageAsync(response);
                if (!response.Successful)
                {
                    LogCompletionFailure(response);
                }
                else
                {
                    var result = response.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";
                    if (IsFourDigitCompanyCode(result))
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

        private async Task<string> ResolveWithAIAsync(string userInput, List<CompanyInfo> companies, CancellationToken cancellationToken)
        {
            try
            {
                var potentialMatches = companies
                    .Where(c => c.Name.Contains(userInput, StringComparison.OrdinalIgnoreCase) || ContainsKeywords(c.Name, userInput))
                    .Take(20)
                    .Select(c => $"{c.Code}: {c.Name}")
                    .ToList();

                if (potentialMatches.Count == 0)
                    return string.Empty;

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

                var userPrompt = $"User input: '{userInput}'\n\nPotential matches:\n{string.Join("\n", potentialMatches)}\n\nReturn the best matching stock code:";

                var completionRequest = new ChatCompletionCreateRequest
                {
                    Messages = [ChatMessage.FromSystem(systemPrompt), ChatMessage.FromUser(userPrompt)],
                    Model = NewModels.GPT_6_LUNA,
                    // gpt-6-luna is a reasoning model: it rejects max_tokens, and its reasoning tokens
                    // count against the cap, so a tight cap meant for the answer alone returns empty.
                    ReasoningEffort = ReasoningEffortType.Low,
                    MaxCompletionTokens = ResolveMaxCompletionTokens
                };

                var response = await _openAIService.ChatCompletion.CreateCompletion(completionRequest);
                await LogUsageAsync(response);
                if (!response.Successful)
                {
                    LogCompletionFailure(response);
                }
                else
                {
                    var result = response.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";
                    if (IsFourDigitCompanyCode(result))
                        return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI resolution failed for input '{userInput}'", userInput);
            }
            return string.Empty;
        }

        private static bool ContainsKeywords(string companyName, string userInput)
        {
            var keywords = userInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return keywords.Any(k => companyName.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        private void SetUserAgent()
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        }

        private class CompanyInfo
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }
    }
}
