using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace JackyAIApp.Server.Services.Portfolio
{
    public class StockCategoryService : IStockCategoryService
    {
        private readonly IDbContextFactory<AzureSQLDBContext> _dbFactory;
        private readonly IOpenAIPromptService _promptService;
        private readonly ITWSEOpenAPIService _twseOpenApi;
        private readonly ILogger<StockCategoryService> _logger;

        private const string PromptPath = "Prompt/Portfolio/CategorySystem.txt";
        private const string Feature = "portfolio.category";

        // Grounds the model in the exact desired output shape (a category name from the fixed
        // enum, a Traditional-Chinese subCategory) — omitting a concrete example is what caused
        // the Listening Part 1 admin generator to default its whole response to English (see
        // that fix's commit history); baked in here from the start instead of discovered later.
        private static readonly (string User, string Assistant) FewShotExample = (
            User: "Symbol: 2330\nName: 台積電\nMarket: TW\nTWSE profile: {\"公司代號\":\"2330\",\"公司名稱\":\"台灣積體電路製造股份有限公司\",\"產業別\":\"24\"}",
            Assistant: JsonConvert.SerializeObject(new CategoryClassificationResult
            {
                Category = nameof(PortfolioCategory.Semiconductor),
                SubCategory = "晶圓代工",
            })
        );

        public StockCategoryService(
            IDbContextFactory<AzureSQLDBContext> dbFactory,
            IOpenAIPromptService promptService,
            ITWSEOpenAPIService twseOpenApi,
            ILogger<StockCategoryService> logger)
        {
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
            _promptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
            _twseOpenApi = twseOpenApi ?? throw new ArgumentNullException(nameof(twseOpenApi));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<(PortfolioCategory Category, string? SubCategory)> GetCategoryAsync(
            AssetType assetType, string symbol, string name, string market, CancellationToken ct = default)
        {
            // Mapped directly, never sent to the AI — see PortfolioCategory's doc comment.
            if (assetType == AssetType.CASH) return (PortfolioCategory.Cash, null);
            if (assetType == AssetType.LIABILITY) return (PortfolioCategory.Liability, null);
            if (string.IsNullOrWhiteSpace(symbol)) return (PortfolioCategory.Other, null);

            // Fresh context per call: this is fanned out across many symbols concurrently by
            // PortfolioService (Task.WhenAll), and a shared scoped DbContext isn't thread-safe for
            // that — this exact bug already bit PersistentTwHistoricalProvider (PR #209).
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var info = await db.StockSymbolInfos.FindAsync([symbol], ct);
            if (info?.Category != null && Enum.TryParse<PortfolioCategory>(info.Category, out var cached))
                return (cached, info.SubCategory);

            var (category, subCategory) = await ClassifyAsync(db, symbol, name, market, ct);

            if (info == null)
            {
                db.StockSymbolInfos.Add(new StockSymbolInfo
                {
                    Symbol = symbol,
                    Market = market,
                    Category = category.ToString(),
                    SubCategory = subCategory,
                    ClassifiedAt = DateTime.UtcNow,
                });
            }
            else
            {
                info.Category = category.ToString();
                info.SubCategory = subCategory;
                info.ClassifiedAt = DateTime.UtcNow;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Concurrent cold-start requests can race to classify/insert the same never-seen
                // symbol — the loser's write can conflict with the winner's; harmless, we still
                // return our own freshly-computed classification to the caller either way.
                _logger.LogDebug(ex, "Race persisting category for {Symbol}, ignoring", symbol);
            }

            return (category, subCategory);
        }

        private async Task<(PortfolioCategory, string?)> ClassifyAsync(
            AzureSQLDBContext db, string symbol, string name, string market, CancellationToken ct)
        {
            var grounding = $"Symbol: {symbol}\nName: {name}\nMarket: {market}";

            // Ground TW symbols in TWSE's real 產業別 industry code — regulatory data that's
            // accurate even for brand-new IPOs, so classification quality doesn't depend on the
            // model's own (possibly stale) knowledge of the company. Best-effort: a failure here
            // just means classifying from name/market alone, not a hard failure.
            if (market == "TW")
            {
                try
                {
                    var profile = await _twseOpenApi.GetCompanyProfileAsync(symbol, ct);
                    if (!string.IsNullOrWhiteSpace(profile))
                        grounding += $"\nTWSE profile: {profile}";
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to fetch TWSE profile for {Symbol}, classifying without it", symbol);
                }
            }

            var existingLabels = await GetExistingSubCategoriesByCategoryAsync(db, ct);
            if (existingLabels.Count > 0)
            {
                var labelsText = string.Join("\n", existingLabels.Select(kv => $"{kv.Key}: {string.Join("、", kv.Value)}"));
                grounding += $"\n\nExisting subCategory labels already in use (grouped by category) — reuse one of these exactly if it fits, per the system prompt's reuse instruction:\n{labelsText}";
            }

            var (result, error) = await _promptService.GetCompletionAsync<CategoryClassificationResult>(
                PromptPath, grounding,
                model: NewModels.GPT_5_NANO, examples: [FewShotExample], feature: Feature);

            if (result == null)
            {
                _logger.LogWarning("Category classification failed for {Symbol}: {Error}", symbol, error);
                return (PortfolioCategory.Other, null);
            }

            // Never trust the model's category string verbatim — it drives a fixed-vocabulary
            // chart, so anything outside the enum is coerced to Other rather than let a
            // hallucinated label leak into the stacked-area series/legend.
            if (!Enum.TryParse<PortfolioCategory>(result.Category, out var category))
            {
                _logger.LogWarning(
                    "Category classification returned unrecognized category {Category} for {Symbol}, using Other",
                    result.Category, symbol);
                category = PortfolioCategory.Other;
            }

            return (category, result.SubCategory);
        }

        /// <summary>Distinct subCategory labels already assigned, grouped by category — grounds the
        /// model toward reusing an existing bucket instead of inventing a fresh variant every call
        /// (each classification is otherwise fully independent with no memory of prior results).</summary>
        private static async Task<Dictionary<string, List<string>>> GetExistingSubCategoriesByCategoryAsync(
            AzureSQLDBContext db, CancellationToken ct)
        {
            var rows = await db.StockSymbolInfos
                .Where(s => s.Category != null && s.SubCategory != null)
                .Select(s => new { s.Category, s.SubCategory })
                .Distinct()
                .ToListAsync(ct);

            return rows
                .GroupBy(r => r.Category!)
                .ToDictionary(g => g.Key, g => g.Select(r => r.SubCategory!).Distinct().ToList());
        }
    }
}
