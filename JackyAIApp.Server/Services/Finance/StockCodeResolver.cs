using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Turns what the user typed (a code, a company's short name, an English name, a nickname) into a
    /// TW stock or ETF code. Names are looked up in the TW symbol list (<see cref="ITwListedCompanyService"/>:
    /// 上市 and 上櫃 stocks and ETFs by the short names people use, held in memory); the model is asked only
    /// when that does not settle it, and its answer is taken only if the code is on that list.
    /// </summary>
    public class StockCodeResolver(
        ILogger<StockCodeResolver> logger,
        IOpenAIPromptService promptService,
        ITwListedCompanyService listedCompanies) : IStockCodeResolver
    {
        private readonly ILogger<StockCodeResolver> _logger = logger;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly ITwListedCompanyService _listedCompanies = listedCompanies;

        internal const string PromptPath = "Prompt/Finance/StockCodeResolverSystem.txt";

        /// <summary>The usage-log label this call has always had (the prompt path would give another).</summary>
        internal const string UsageFeature = "finance.stockcode_resolver";

        public async Task<string> ResolveStockCodeAsync(string userInput, CancellationToken cancellationToken = default)
        {
            try
            {
                // Input that is already shaped like a TW code (incl. 5-6 digit ETFs such as 00878 / 00631L)
                // is the answer.
                if (TwStockCode.TryNormalize(userInput, out var code))
                    return code;

                var name = (userInput ?? "").Trim();
                if (name.Length == 0)
                    return string.Empty;

                // The listed names that contain the input (台積 → 台積電), by code.
                var candidates = await _listedCompanies.SearchAsync(name, cancellationToken);

                var exact = candidates.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                    return exact.Symbol;
                if (candidates.Count == 1)
                    return candidates[0].Symbol;

                // Several names contain it, or none does (an English name, a nickname, a full legal name):
                // the model picks.
                var answer = await AskModelAsync(name, candidates);
                if (answer.Length > 0 && await _listedCompanies.GetMarketTypeAsync(answer, cancellationToken) != null)
                {
                    _logger.LogInformation("LLM resolved '{input}' to stock code '{code}'", name, answer);
                    return answer;
                }

                if (answer.Length > 0)
                    _logger.LogWarning("LLM answered '{code}' for '{input}', which is not a listed code", answer, name);
                else
                    _logger.LogWarning("Could not resolve user input '{userInput}' to a stock code", name);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving stock code for input '{userInput}'", userInput);
                return userInput;
            }
        }

        /// <summary>
        /// Asks the model which company or ETF the user means, given the <paramref name="candidates"/>
        /// (possibly none). Empty when it names none, or anything not shaped like a TW code. A failed
        /// call is logged with its reason: otherwise the user only sees "not found".
        /// </summary>
        private async Task<string> AskModelAsync(string userInput, IReadOnlyCollection<StockSearchItem> candidates)
        {
            var list = candidates.Count == 0
                ? "(none of the listed names contains the input)"
                : string.Join("\n", candidates.Select(c => $"{c.Symbol}: {c.Name}"));
            var userMessage = $"User input: '{userInput}'\n\nCandidates:\n{list}";
            var (answer, error) = await _promptService.GetCompletionAsync<StockCodeResolution>(
                PromptPath, userMessage, model: NewModels.GPT_6_LUNA, feature: UsageFeature);
            if (answer is null)
            {
                _logger.LogWarning("Stock-code resolution failed for '{userInput}': {Error}", userInput, error);
                return string.Empty;
            }

            return TwStockCode.TryNormalize(answer.StockCode, out var code) ? code : string.Empty;
        }
    }
}
