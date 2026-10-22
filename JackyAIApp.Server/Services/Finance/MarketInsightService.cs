using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Finance
{
    public class MarketInsightService(
        ILogger<MarketInsightService> logger,
        ICurrentUser currentUser,
        IMemoryCache memoryCache,
        IUsageLimitService usageLimitService,
        IFinnhubNewsProvider finnhubNewsProvider,
        ITWSEDataService twseDataService,
        IOpenAIPromptService openAIPromptService,
        IMarketDataQueryService marketData) : IMarketInsightService
    {
        private readonly ILogger<MarketInsightService> _logger = logger;
        private readonly ICurrentUser _currentUser = currentUser;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly IUsageLimitService _usageLimitService = usageLimitService;
        private readonly IFinnhubNewsProvider _finnhubNewsProvider = finnhubNewsProvider;
        private readonly ITWSEDataService _twseDataService = twseDataService;
        private readonly IOpenAIPromptService _openAIPromptService = openAIPromptService;
        // The cached market-overview and indicator sets this service grounds its prompts in; reused
        // (not re-fetched) so the digest rides the same 3-minute overview cache every visitor shares.
        private readonly IMarketDataQueryService _marketData = marketData;

        private const int CACHE_HOURS = 12;

        /// <summary>
        /// "今日市場AI摘要" — a unified daily digest grounding gpt-6-luna in three sources
        /// (market overview + global news + TW major announcements) and synthesising a
        /// global/Taiwan two-section narrative. The generated digest is shared (cached per
        /// day), but each user is charged <see cref="UsageFeature.MarketDigest"/> once per day
        /// to unlock it — repeat views the same day are free.
        /// </summary>
        public async Task<ServiceResult<MarketDigestResponse>> GetMarketDigestAsync(CancellationToken cancellationToken)
        {
            var userId = _currentUser.RequireId();
            var today = DateTime.Now.ToString("yyyyMMdd");

            // 1) Ensure today's digest content exists (shared across users).
            var contentKey = $"MarketDigest_{today}";
            var digest = _memoryCache.TryGetValue(contentKey, out object? cached) ? cached as MarketDigestResponse : null;
            if (digest is null)
            {
                var generated = await GenerateMarketDigestAsync(cancellationToken);
                if (!generated.IsSuccess)
                    return generated.AsFailure();
                digest = generated.Data;
                _memoryCache.Set(contentKey, digest, TimeSpan.FromHours(CACHE_HOURS));
            }

            // 2) Charge once per user per day (cached reads the same day are free).
            var chargeKey = $"MarketDigestCharged_{userId}_{today}";
            if (!_memoryCache.TryGetValue(chargeKey, out object? _))
            {
                var ok = await _usageLimitService.TryConsumeAsync(userId, UsageFeature.MarketDigest);
                if (!ok)
                    return ServiceResult.Fail(ErrorCodes.UsageLimitExceeded, "Daily usage limit exceeded.");
                // Hold the guard until end of day so the user isn't charged again today.
                _memoryCache.Set(chargeKey, true, DateTime.Today.AddDays(1) - DateTime.Now);
            }

            return ServiceResult.Ok(digest);
        }

        /// <summary>
        /// Gathers the three grounding sources in parallel and calls gpt-6-luna to produce the
        /// unified digest. Pure generation — no caching or usage-limit logic.
        /// </summary>
        private async Task<ServiceResult<MarketDigestResponse>> GenerateMarketDigestAsync(CancellationToken cancellationToken)
        {
            // Fetch the three sources in parallel. News + TW announcements are best-effort.
            var overviewTask = _marketData.GetMarketOverviewAsync(cancellationToken);
            var newsTask = _finnhubNewsProvider.IsConfigured
                ? _finnhubNewsProvider.FetchAsync(cancellationToken)
                : Task.FromResult<IReadOnlyList<FinnhubNewsItem>>([]);
            var twseTask = GetTwseMajorNewsTextSafeAsync(cancellationToken);

            await Task.WhenAll(overviewTask, newsTask, twseTask);

            var overviewResult = overviewTask.Result;
            if (!overviewResult.IsSuccess)
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, overviewResult.ErrorMessage);

            var overview = overviewResult.Data;
            if (overview.Quotes.Count == 0)
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, "No market data available for the digest.");

            var dataText = string.Join("\n", overview.Quotes.Select(q =>
            {
                var price = q.Price?.ToString("0.##") ?? "N/A";
                var pct = q.ChangePercent.HasValue
                    ? $" ({(q.ChangePercent.Value >= 0 ? "+" : "")}{q.ChangePercent.Value:F2}%)"
                    : "";
                return $"{q.Name}: {price}{pct}";
            }));

            var news = newsTask.Result;
            var newsSection = news.Count > 0
                ? "\n\n全球財經新聞（用於解讀全球市場移動原因）：\n" +
                  string.Join("\n", news.Select((n, i) =>
                      $"{i + 1}. [{n.Source}] {n.Headline}" +
                      (string.IsNullOrWhiteSpace(n.Summary) ? "" : $"：{n.Summary}")))
                : "";

            var twseSection = string.IsNullOrWhiteSpace(twseTask.Result)
                ? ""
                : $"\n\n台股今日公司公告（證交所公開資訊觀測站重大訊息，多為更名/處分資產/董事會決議/契約等公開公告，非投資訊號；日期為民國年需 +1911 轉西元）：\n{twseTask.Result}";

            var systemPrompt =
                "你是專業市場策略師，為台灣投資人整理『今日市場AI摘要』。根據提供的三類資料（全球市場數據、全球財經新聞、台股公司公告），" +
                "整理成兩區重點：\n" +
                "- category=\"global\"（全球總經）：3~5 點，挖掘數字與新聞背後的因果與情緒，不要只複述數字。\n" +
                "- category=\"taiwan\"（台股公司動態）：2~4 點。注意這些是公開公告（更名、處分股權/資產、董事會決議、重大契約、併購等），" +
                "不是投資訊號；請挑出較具實質意義的（如併購、處分重大資產、重大契約、營運變動），過濾純例行（補充說明、財報公布日、更名等行政事項），" +
                "並如實描述公告內容、不要誇大成買賣建議；多數情況 sentiment 用 neutral，除非公告明顯偏多或偏空。若是特定公司，填 stockCode。\n" +
                "每點：headline（一句標題）+ detail（一句說明）+ sentiment（bullish|bearish|neutral）+ category。" +
                "只根據提供的資料推論，不編造未提供的事件。全部用繁體中文。" +
                "只回傳 JSON：{\"points\":[{\"category\":\"global|taiwan\",\"headline\":\"\",\"detail\":\"\",\"sentiment\":\"bullish|bearish|neutral\",\"stockCode\":\"\"}]}。";
            var userMessage = $"全球市場數據：\n{dataText}{newsSection}{twseSection}";

            try
            {
                var (result, error) = await _openAIPromptService.GetCompletionWithSystemPromptAsync<MarketDigestResponse>(
                    systemPrompt, userMessage, NewModels.GPT_6_LUNA, feature: "finance.market_digest");

                if (result is null || result.Points.Count == 0)
                    return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, error ?? "Failed to generate market digest.");

                result.GeneratedAt = DateTime.UtcNow;
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate market digest");
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, "Failed to generate market digest.");
            }
        }

        /// <summary>
        /// Fetches today's TWSE major-announcement filings and trims them to a compact, token-bounded
        /// text block for the digest prompt. Best-effort: returns empty on any failure.
        /// </summary>
        private async Task<string> GetTwseMajorNewsTextSafeAsync(CancellationToken cancellationToken)
        {
            const int MAX_ITEMS = 60;
            try
            {
                var raw = await _twseDataService.GetRawMaterialInfoWithCacheAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(raw))
                    return "";

                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                    return "";

                var lines = new List<string>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (lines.Count >= MAX_ITEMS) break;

                    string Get(params string[] keys)
                    {
                        foreach (var k in keys)
                            if (item.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                                return v.GetString() ?? "";
                        return "";
                    }

                    var code = Get("公司代號", "Code", "CompanyCode");
                    var name = Get("公司名稱", "Name", "CompanyName");
                    var subject = Get("主旨", "Subject", "Title");
                    if (string.IsNullOrWhiteSpace(subject)) continue;

                    lines.Add($"{code} {name}：{subject}".Trim());
                }

                return string.Join("\n", lines);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build TWSE major-news text for digest");
                return "";
            }
        }

        public async Task<ServiceResult<IndicatorInsight>> GetIndicatorInsightAsync(string id, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(id))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Indicator id is required.");

            var cacheKey = $"IndicatorInsight_{id}_{DateTime.Today:yyyyMMdd}";
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedEntry) && cachedEntry is IndicatorInsight cached)
                return ServiceResult.Ok(cached);

            // Reuse the cached indicator set to build the prompt context.
            var indicatorsResult = await _marketData.GetIndicatorsAsync(cancellationToken);
            if (!indicatorsResult.IsSuccess)
                return indicatorsResult.AsFailure();

            var indicators = indicatorsResult.Data;
            var series = indicators.Indicators.FirstOrDefault(i => i.Id == id);
            if (series is null)
                return ServiceResult.Fail(ErrorCodes.NotFound, $"Indicator '{id}' not found.");

            // Summarise recent points (first / mid / last) so the prompt stays small.
            var pts = series.Points;
            var sampled = pts.Count <= 8 ? pts : new[] { pts[0], pts[pts.Count / 2], pts[^1] }.ToList();
            var pointsText = string.Join("\n", sampled.Select(p => $"{p.Date}: {p.Value}{series.Unit}"));

            var systemPrompt =
                "你是專業總體經濟分析師。根據提供的單一指標近期數據，用繁體中文寫 2~3 句白話解讀：" +
                "它目前在說什麼、趨勢方向、以及對市場或投資人的意涵。避免空泛、給具體含義。" +
                "只回傳 JSON：{\"insight\":\"...\"}。";
            var userMessage =
                $"指標：{series.Name}（{series.Description}）\n單位：{series.Unit}\n最新：{series.LatestValue}（{series.LatestDate}）\n近期數據：\n{pointsText}";

            try
            {
                var (result, error) = await _openAIPromptService.GetCompletionWithSystemPromptAsync<IndicatorInsight>(
                    systemPrompt, userMessage, NewModels.GPT_6_LUNA, feature: "finance.indicator_insight");

                if (result is null)
                    return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, error ?? "Failed to generate insight.");

                _memoryCache.Set(cacheKey, result, TimeSpan.FromHours(24));
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate indicator insight for {id}", id);
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Failed to generate insight.");
            }
        }
    }
}
