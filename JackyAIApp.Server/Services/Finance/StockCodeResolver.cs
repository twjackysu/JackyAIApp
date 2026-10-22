using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance
{
    public class StockCodeResolver(
        IHttpClientFactory httpClientFactory,
        ILogger<StockCodeResolver> logger,
        IOpenAIPromptService promptService,
        IMemoryCache memoryCache) : IStockCodeResolver
    {
        private readonly HttpClient _httpClient = httpClientFactory.CreateClient("ExternalReadOnly");
        private readonly ILogger<StockCodeResolver> _logger = logger;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly IMemoryCache _memoryCache = memoryCache;

        private const string BaseUrl = "https://openapi.twse.com.tw/v1";
        private const string UserAgent = "stock-analysis/1.0";
        private const string CompanyListCacheKey = "twse_company_list";

        internal const string PromptPath = "Prompt/Finance/StockCodeResolverSystem.txt";

        /// <summary>The usage-log label this call has always had (the prompt path would give another).</summary>
        internal const string UsageFeature = "finance.stockcode_resolver";

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

                var exactMatch = await FindExactCompanyMatchAsync(companies, userInput);
                if (!string.IsNullOrEmpty(exactMatch))
                    return exactMatch;

                var aiResolvedCode = await ResolveWithAIAsync(userInput, companies);
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

        /// <summary>
        /// Asks the model which company the user means, given <paramref name="companies"/> (both steps
        /// share the one prompt). Empty when it names none, or anything but a 4-digit code. A failed
        /// call is logged with its reason: otherwise the user only sees "not found".
        /// </summary>
        private async Task<string> AskModelAsync(string userInput, IEnumerable<CompanyInfo> companies)
        {
            var userMessage = $"User input: '{userInput}'\n\nCompanies:\n{string.Join("\n", companies.Select(c => $"{c.Code}: {c.Name}"))}";
            var (answer, error) = await _promptService.GetCompletionAsync<StockCodeResolution>(
                PromptPath, userMessage, model: NewModels.GPT_6_LUNA, feature: UsageFeature);
            if (answer is null)
            {
                _logger.LogWarning("Stock-code resolution failed for '{userInput}': {Error}", userInput, error);
                return string.Empty;
            }

            var code = answer.StockCode?.Trim() ?? "";
            return IsFourDigitCompanyCode(code) ? code : string.Empty;
        }

        private Task<List<CompanyInfo>> GetCompanyListAsync(CancellationToken cancellationToken) =>
            // Every resolution reads the same list: one fetch for a burst of them. An empty list (the
            // fetch failed, or nothing parsed) is returned but not cached, so the next call tries again.
            _memoryCache.GetOrCreateSingleFlightAsync(CompanyListCacheKey, TimeSpan.FromHours(24),
                FetchCompanyListAsync, cancellationToken, shouldCache: companies => companies.Count > 0);

        private async Task<List<CompanyInfo>> FetchCompanyListAsync()
        {
            // Shared by every caller waiting on it, so no one caller's token may stop it.
            var cancellationToken = CancellationToken.None;
            try
            {
                SetUserAgent();
                using var response = await _httpClient.GetAsync($"{BaseUrl}/opendata/t187ap03_L", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    return ParseCompanyList(content);
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

        private async Task<string> FindExactCompanyMatchAsync(List<CompanyInfo> companies, string userInput)
        {
            var exact = companies.FirstOrDefault(c => string.Equals(c.Name, userInput, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact.Code;

            // The first 100 companies by code are mostly 1xxx and rarely the one meant. What makes this
            // step work is the model knowing common short names (台積電 → 2330), which the substring
            // search in ResolveWithAIAsync cannot find: 台積電 is not part of 台灣積體電路製造.
            var result = await AskModelAsync(userInput, companies.Take(100));
            if (result.Length > 0)
                _logger.LogInformation("LLM resolved '{input}' to stock code '{code}'", userInput, result);
            return result;
        }

        private async Task<string> ResolveWithAIAsync(string userInput, List<CompanyInfo> companies)
        {
            var potentialMatches = companies
                .Where(c => c.Name.Contains(userInput, StringComparison.OrdinalIgnoreCase) || ContainsKeywords(c.Name, userInput))
                .Take(20)
                .ToList();

            return potentialMatches.Count == 0 ? string.Empty : await AskModelAsync(userInput, potentialMatches);
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
