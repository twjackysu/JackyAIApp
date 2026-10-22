using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using JackyAIApp.Server.Services.OpenAI;

namespace JackyAIApp.Server.Services.Finance
{
    public class StockCodeResolver(
        ILogger<StockCodeResolver> logger,
        IOpenAIPromptService promptService,
        ITwseOpenApi twse) : IStockCodeResolver
    {
        private readonly ILogger<StockCodeResolver> _logger = logger;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly ITwseOpenApi _twse = twse;

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

        /// <summary>The 上市 companies (code and name); empty when TWSE's list could not be read.</summary>
        private async Task<List<CompanyInfo>> GetCompanyListAsync(CancellationToken cancellationToken) =>
            (await _twse.GetCompanyProfilesAsync(cancellationToken) ?? [])
                .Where(p => p.Code.Length > 0 && p.Name.Length > 0)
                .Select(p => new CompanyInfo { Code = p.Code, Name = p.Name })
                .ToList();

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

        private class CompanyInfo
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }
    }
}
