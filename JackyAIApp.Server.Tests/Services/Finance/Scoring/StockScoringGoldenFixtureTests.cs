using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Builder;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.DataProviders.US;
using JackyAIApp.Server.Services.Finance.Indicators;
using JackyAIApp.Server.Services.Finance.Scoring;
using Microsoft.Extensions.Logging;
using Moq;
using static JackyAIApp.Server.DTO.Finance.IndicatorCategory;
using static JackyAIApp.Server.DTO.Finance.SignalDirection;

namespace JackyAIApp.Server.Tests.Services.Finance.Scoring
{
    /// <summary>
    /// TASK-056 (Issue #464 IMP-2 step 1): freezes the output of the production C# scoring/risk path
    /// into <c>stockScoringGolden.json</c>, which TASK-057 ports to TypeScript and checks against.
    ///
    /// Every expected value comes from the real <see cref="StockAnalysisBuilder.BuildAsync"/> — the
    /// same call chain <c>FinanceQueryService.GetComprehensiveAnalysisAsync</c> uses — with only the
    /// data providers and the indicator engine mocked. This class deliberately contains no copy of
    /// the scoring rules, so a hand-written "expected" can never drift from what production does.
    ///
    /// Default mode compares the regenerated JSON with the committed file and fails on any
    /// difference. <c>UPDATE_GOLDEN=1</c> rewrites the file instead. When TASK-058 deletes the C#
    /// scoring, this class is deleted with it and the JSON stays (it is then the only record).
    /// </summary>
    public class StockScoringGoldenFixtureTests
    {
        private const string UpdateEnvVar = "UPDATE_GOLDEN";

        private const string GoldenRelativePath = "jackyaiapp.client/src/Finance/utils/__fixtures__/stockScoringGolden.json";

        /// <summary>
        /// Sized so the committed file stays near 500 KB (TASK-056 requirement 7): every indicator is
        /// written twice (input and filtered output), about 260 bytes together, so a case with 30
        /// indicators is ~8 KB and the 300 the task first suggested would be over 1 MB.
        /// </summary>
        private const int RandomCaseCount = 90;

        private const ulong RandomSeed = 0x2545F4914F6CDD1DUL;

        // Master tip when the file was generated; the three scoring sources below were last changed
        // by 0d03215 (PR #437), so the C# behind this file is unchanged between the two.
        private const string SourceCommit = "55e50dbf65202779ab72c75679e372682229e891";
        private const string ScoringSourcesLastChangedIn = "0d0321585abc6aca379e93a5663377cc8304fb69";

        private static readonly GoldenConfig DefaultConfig = new(
            IncludeTechnical: true, IncludeChip: true, IncludeFundamental: true,
            IncludeScoring: true, IncludeRisk: true,
            TechnicalWeight: 0.5m, ChipWeight: 0.3m, FundamentalWeight: 0.2m);

        /// <summary>
        /// Same settings as <c>Program.cs</c> (<c>AddControllers().AddJsonOptions</c>) on top of
        /// the ASP.NET Core web defaults: camelCase, enums as strings, cycles ignored. The only
        /// difference is the encoder: relaxed escaping keeps Chinese readable in the file; a JSON
        /// parser reads both forms identically.
        /// </summary>
        private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() },
        };

        private static readonly Lazy<Task<List<GoldenCase>>> AllCases = new(BuildAllCasesAsync);

        #region Tests

        [Fact]
        public async Task GoldenFile_MatchesWhatTheCurrentCSharpProduces()
        {
            var generated = SerializeGoldenFile(await AllCases.Value);
            var path = Path.Combine(FindRepoRoot(), GoldenRelativePath);

            if (Environment.GetEnvironmentVariable(UpdateEnvVar) == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return;
            }

            Assert.True(File.Exists(path),
                $"找不到黃金資料 {GoldenRelativePath}。用 {UpdateEnvVar}=1 執行本測試類別來產生它。");

            var committed = File.ReadAllText(path).Replace("\r\n", "\n");
            if (committed == generated) return;

            Assert.Fail(DescribeFirstDifference(committed, generated));
        }

        [Fact]
        public async Task CaseSet_StillExercisesTheTrapsItWasDesignedFor()
        {
            var cases = await AllCases.Value;
            var byId = cases.ToDictionary(c => c.Id);

            // The case list itself.
            Assert.Equal(cases.Count, byId.Count); // ids are unique
            Assert.True(cases.Count(c => !c.Id.StartsWith("rand-")) >= 55, "手工邊界案例應約 60 個");
            Assert.Equal(RandomCaseCount, cases.Count(c => c.Id.StartsWith("rand-")));

            // Every outcome the frontend port has to reproduce shows up at least once.
            var scorings = cases.Select(c => c.Expected.Scoring).Where(s => s is not null).Select(s => s!).ToList();
            foreach (var direction in Enum.GetValues<SignalDirection>())
            {
                Assert.Contains(scorings, s => s.OverallDirection == direction);
                Assert.Contains(scorings.SelectMany(s => s.CategoryScores), c => c.Direction == direction);
            }
            foreach (var level in Enum.GetValues<RiskLevel>())
                Assert.Contains(cases, c => c.Expected.Risk?.Level == level);
            Assert.Contains(cases, c => c.Expected.Scoring is null && c.Expected.Risk is null);
            Assert.Contains(cases, c => c.Expected.Scoring is null && c.Expected.Risk is not null);
            Assert.Contains(cases, c => c.Expected.Scoring is not null && c.Expected.Risk is null);

            // Each trap below is hand-calculated from the rule it pins, not copied from the output.
            // If a case is edited so that it stops hitting its trap, this fails.
            // Category direction uses the UNROUNDED average (79.95 displays as 80.0 but is not >= 80).
            var techBullish = Cat(byId["cat-avg-79_95-shows-80_0-but-bullish"], Technical);
            Assert.Equal(80.0m, techBullish.Score);
            Assert.Equal(Bullish, techBullish.Direction);
            var techStrongBearish = Cat(byId["cat-avg-19_95-shows-20_0-but-strong-bearish"], Technical);
            Assert.Equal(20.0m, techStrongBearish.Score);
            Assert.Equal(StrongBearish, techStrongBearish.Direction);

            // Math.Round(decimal, 1) is banker's rounding.
            Assert.Equal(62.2m, Cat(byId["round-avg-62_25-half-even"], Technical).Score);
            Assert.Equal(62.8m, Cat(byId["round-avg-62_75-half-even"], Technical).Score);
            Assert.Equal(60.0m, Cat(byId["cat-avg-60_05-half-even-rounds-down"], Technical).Score);
            Assert.Equal(12.4m, Cat(byId["round-weighted-12_45-half-even"], Technical).WeightedScore);
            Assert.Equal(12.6m, Cat(byId["round-weighted-12_55-half-even"], Technical).WeightedScore);

            // Overall score is the sum of the ROUNDED weighted scores; 29.95 + 18 + 12 would be 59.95.
            var roundedSum = byId["overall-sum-of-rounded-59_95-is-60"].Expected.Scoring!;
            Assert.Equal(60.0m, roundedSum.OverallScore);
            Assert.Equal(Bullish, roundedSum.OverallDirection);

            // Overall boundaries.
            AssertOverall(byId, "overall-80-exact", 80.0m, StrongBullish);
            AssertOverall(byId, "overall-79_9", 79.9m, Bullish);
            AssertOverall(byId, "overall-60-exact", 60.0m, Bullish);
            AssertOverall(byId, "overall-59_9", 59.9m, Neutral);
            AssertOverall(byId, "overall-40-exact", 40.0m, Neutral);
            AssertOverall(byId, "overall-39_9", 39.9m, Bearish);
            AssertOverall(byId, "overall-20-exact", 20.0m, Bearish);
            AssertOverall(byId, "overall-19_9", 19.9m, StrongBearish);

            // Weights: all zero is NOT normalized; absent categories hand their weight to the rest.
            var allZero = byId["weights-all-zero"].Expected.Scoring!;
            Assert.Equal(0m, allZero.OverallScore);
            Assert.Equal(StrongBearish, allZero.OverallDirection);
            Assert.All(allZero.CategoryScores, category => Assert.Equal(0m, category.WeightedScore));
            Assert.Equal(0m, byId["weights-only-present-category-has-zero-weight"].Expected.Scoring!.OverallScore);
            var redistributed = byId["weights-chip-missing-redistributed"].Expected.Scoring!.CategoryScores;
            Assert.Equal(2, redistributed.Count);
            Assert.InRange(redistributed.Sum(category => category.Weight), 0.999999m, 1.000001m);

            // Order of categoryScores = first appearance of each category.
            Assert.Equal(new[] { Fundamental, Technical, Chip },
                byId["order-fundamental-first"].Expected.Scoring!.CategoryScores.Select(category => category.Category));
            Assert.Equal(new[] { Chip, Technical, Fundamental },
                byId["order-interleaved-chip-first"].Expected.Scoring!.CategoryScores.Select(category => category.Category));

            // Flags.
            Assert.Null(byId["flag-all-categories-off"].Expected.Scoring);
            Assert.Null(byId["flag-all-categories-off"].Expected.Risk);
            Assert.Null(byId["flag-risk-off"].Expected.Risk);
            Assert.NotNull(byId["flag-risk-off"].Expected.Scoring);
            Assert.Null(byId["flag-scoring-off-risk-on-category-gap-factor-absent"].Expected.Scoring);
            Assert.DoesNotContain(CategoryGapFactor, byId["flag-scoring-off-risk-on-category-gap-factor-absent"].Expected.Risk!.Factors);
            Assert.Contains(CategoryGapFactor, byId["flag-scoring-on-risk-on-category-gap-factor-present"].Expected.Risk!.Factors);

            // Risk thresholds are strict (>), and the number format is decimal F1 (half away from zero).
            Assert.DoesNotContain(byId["risk-rsi-80-exact-no-factor"].Expected.Risk!.Factors, f => f.StartsWith("RSI="));
            Assert.Contains("RSI=80.1，嚴重超買", byId["risk-rsi-80_1-overbought"].Expected.Risk!.Factors);
            Assert.DoesNotContain(byId["risk-rsi-20-exact-no-factor"].Expected.Risk!.Factors, f => f.StartsWith("RSI="));
            Assert.Contains("RSI=19.9，嚴重超賣", byId["risk-rsi-19_9-oversold"].Expected.Risk!.Factors);
            Assert.Contains("RSI=80.1，嚴重超買", byId["risk-rsi-80_05-f1-rounds-away-from-zero"].Expected.Risk!.Factors);
            Assert.Contains("RSI=80.4，嚴重超買", byId["risk-rsi-80_35-f1-rounds-away-from-zero"].Expected.Risk!.Factors);
            Assert.Contains("RSI=20.0，嚴重超賣", byId["risk-rsi-19_95-f1-rounds-up-to-20_0"].Expected.Risk!.Factors);
            Assert.Empty(byId["risk-rsi-duplicate-first-wins-neutral-first"].Expected.Risk!.Factors);
            Assert.Single(byId["risk-rsi-duplicate-first-wins-overbought-first"].Expected.Risk!.Factors);
            Assert.Empty(byId["risk-rsi-name-is-case-sensitive"].Expected.Risk!.Factors);
            Assert.Empty(byId["risk-volume-3-exact-no-factor"].Expected.Risk!.Factors);
            Assert.Contains("今日成交量為20日均量的3.1倍，量能異常", byId["risk-volume-3_1"].Expected.Risk!.Factors);
            Assert.Contains("今日成交量為20日均量的3.1倍，量能異常", byId["risk-volume-3_05-f1-rounds-away-from-zero"].Expected.Risk!.Factors);
            Assert.Empty(byId["risk-volume-missing-subvalue-no-factor"].Expected.Risk!.Factors);
            Assert.Empty(byId["risk-volume-subvalue-key-is-case-sensitive"].Expected.Risk!.Factors);
            Assert.Empty(byId["risk-pledge-30-exact-no-factor"].Expected.Risk!.Factors);
            Assert.Contains("董監設質比率=30.1%，偏高", byId["risk-pledge-30_1"].Expected.Risk!.Factors);
            Assert.Contains("董監設質比率=30.1%，偏高", byId["risk-pledge-30_05-f1-rounds-away-from-zero"].Expected.Risk!.Factors);

            // Divergence thresholds (min(bull,bear)/total*200; >30, >50, >60 are strict).
            AssertDivergence(byId, "risk-div-30-exact-no-factor", 30.0m, RiskLevel.Low, factors: 0);
            AssertDivergence(byId, "risk-div-30_77-moderate", 30.8m, RiskLevel.Medium, factors: 1);
            AssertDivergence(byId, "risk-div-50-exact-medium", 50.0m, RiskLevel.Medium, factors: 1);
            AssertDivergence(byId, "risk-div-57-high-by-divergence-alone", 57.1m, RiskLevel.High, factors: 1);
            AssertDivergence(byId, "risk-div-60-exact-moderate-not-severe", 60.0m, RiskLevel.High, factors: 1);
            AssertDivergence(byId, "risk-div-61_5-severe", 61.5m, RiskLevel.High, factors: 1);
            Assert.StartsWith("指標存在分歧", byId["risk-div-60-exact-moderate-not-severe"].Expected.Risk!.Factors[0]);
            Assert.StartsWith("指標嚴重分歧", byId["risk-div-61_5-severe"].Expected.Risk!.Factors[0]);

            // Level by factor count.
            Assert.Equal(RiskLevel.Low, byId["risk-factors-0-low"].Expected.Risk!.Level);
            Assert.Equal(RiskLevel.Medium, byId["risk-factors-1-medium"].Expected.Risk!.Level);
            Assert.Equal(RiskLevel.Medium, byId["risk-factors-2-medium"].Expected.Risk!.Level);
            Assert.Equal(RiskLevel.High, byId["risk-factors-3-high"].Expected.Risk!.Level);
            Assert.Equal(RiskLevel.VeryHigh, byId["risk-factors-4-very-high"].Expected.Risk!.Level);
            Assert.Equal(RiskLevel.VeryHigh, byId["risk-factors-5-all-in-order"].Expected.Risk!.Level);
            Assert.Equal(5, byId["risk-factors-5-all-in-order"].Expected.Risk!.Factors.Count);
            Assert.Equal(0m, byId["risk-factors-4-very-high"].Expected.Risk!.DivergenceScore);
            Assert.Equal(2, byId["risk-high-by-divergence-over-50-with-two-factors"].Expected.Risk!.Factors.Count);
            Assert.Equal(RiskLevel.High, byId["risk-high-by-divergence-over-50-with-two-factors"].Expected.Risk!.Level);

            // Category gap (> 30, measured on the ROUNDED category scores).
            Assert.DoesNotContain(CategoryGapFactor, byId["gap-30-exact-no-factor"].Expected.Risk!.Factors);
            Assert.Contains(CategoryGapFactor, byId["gap-30_1-factor"].Expected.Risk!.Factors);
            Assert.Equal(80.0m, Cat(byId["gap-uses-rounded-scores-80_05-vs-50"], Technical).Score);
            Assert.DoesNotContain(CategoryGapFactor, byId["gap-uses-rounded-scores-80_05-vs-50"].Expected.Risk!.Factors);
            Assert.DoesNotContain(CategoryGapFactor, byId["gap-single-category-no-factor"].Expected.Risk!.Factors);
            Assert.Contains(CategoryGapFactor, byId["gap-three-categories-uses-max-and-min"].Expected.Risk!.Factors);

            // Filtering happens before the risk checks.
            Assert.DoesNotContain(byId["flag-technical-off-drops-rsi-factor"].Expected.Risk!.Factors, f => f.StartsWith("RSI="));
            Assert.DoesNotContain(byId["flag-chip-off-drops-pledge-factor"].Expected.Risk!.Factors, f => f.StartsWith("董監設質"));

            // Recommendation wording: five directions and the two divergence notes.
            Assert.StartsWith("強烈看多", byId["rec-strong-bullish"].Expected.Scoring!.Recommendation);
            Assert.StartsWith("偏多操作", byId["rec-bullish"].Expected.Scoring!.Recommendation);
            Assert.StartsWith("中性觀望", byId["rec-neutral"].Expected.Scoring!.Recommendation);
            Assert.StartsWith("偏空操作", byId["rec-bearish"].Expected.Scoring!.Recommendation);
            Assert.StartsWith("強烈看空", byId["rec-strong-bearish"].Expected.Scoring!.Recommendation);
            Assert.EndsWith("可能存在出貨風險。", byId["rec-technical-bullish-chip-bearish-warning"].Expected.Scoring!.Recommendation);
            Assert.EndsWith("主力可能正在佈局。", byId["rec-technical-bearish-chip-bullish-hint"].Expected.Scoring!.Recommendation);
            Assert.EndsWith("可能存在出貨風險。", byId["rec-technical-strong-bullish-chip-strong-bearish-warning"].Expected.Scoring!.Recommendation);
            Assert.EndsWith("主力可能正在佈局。", byId["rec-technical-strong-bearish-chip-strong-bullish-hint"].Expected.Scoring!.Recommendation);
            foreach (var id in new[] { "rec-technical-bullish-chip-bullish-no-note", "rec-technical-neutral-chip-bearish-no-note",
                         "rec-technical-only-no-note", "rec-chip-only-no-note", "rec-fundamental-is-ignored-by-the-notes" })
            {
                var text = byId[id].Expected.Scoring!.Recommendation;
                Assert.DoesNotContain("出貨風險", text);
                Assert.DoesNotContain("主力可能", text);
            }
        }

        #endregion

        #region Generation (the production path)

        private static async Task<List<GoldenCase>> BuildAllCasesAsync()
        {
            // The risk factors format numbers with the current culture; production runs on Linux
            // with the invariant culture. Pin it so the file is the same on every developer machine.
            var previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                var inputs = HandCraftedCases().Concat(RandomCases()).ToList();

                var marketProvider = new Mock<IMarketDataProvider>();
                var chipProvider = new Mock<IChipDataProvider>();
                var fundamentalProvider = new Mock<IFundamentalDataProvider>();
                var providerFactory = new Mock<IMarketDataProviderFactory>();
                var engine = new Mock<IIndicatorEngine>();
                var insiderProvider = new Mock<SECInsiderTradingProvider>(
                    Mock.Of<IHttpClientFactory>(),
                    Mock.Of<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                    Mock.Of<ILogger<SECInsiderTradingProvider>>());

                marketProvider.Setup(p => p.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MinimalMarketData());
                chipProvider.Setup(p => p.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new MarketData { Chips = new ChipData() });
                fundamentalProvider.Setup(p => p.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new FundamentalData { PERatio = 20, PBRatio = 3.5m, DividendYield = 2.5m });
                providerFactory.Setup(f => f.DetectRegion(It.IsAny<string>())).Returns(MarketRegion.TW);
                providerFactory.Setup(f => f.GetChipDataProvider(MarketRegion.TW)).Returns(chipProvider.Object);

                // The engine hands back each case's own indicator list; the builder does the rest.
                List<IndicatorResult> current = [];
                engine.Setup(e => e.CalculateAll(It.IsAny<IndicatorContext>())).Returns(() => current);

                var cases = new List<GoldenCase>(inputs.Count);
                foreach (var input in inputs)
                {
                    current = input.Indicators;
                    var cfg = input.Config;

                    var builder = new StockAnalysisBuilder(
                        marketProvider.Object, chipProvider.Object, fundamentalProvider.Object,
                        providerFactory.Object, engine.Object, new CategoryWeightConfig(),
                        Mock.Of<ILogger<StockAnalysisBuilder>>(), insiderProvider.Object,
                        Mock.Of<IHttpClientFactory>());

                    // Same calls, same order, as FinanceQueryService.GetComprehensiveAnalysisAsync.
                    var result = await builder
                        .ForStock("2330")
                        .ForMarket(MarketRegion.TW)
                        .WithTechnical(cfg.IncludeTechnical)
                        .WithChip(cfg.IncludeChip)
                        .WithFundamental(cfg.IncludeFundamental)
                        .WithScoring(cfg.IncludeScoring)
                        .WithRisk(cfg.IncludeRisk)
                        .WithCustomWeights(cfg.TechnicalWeight, cfg.ChipWeight, cfg.FundamentalWeight)
                        .BuildAsync();

                    var scoring = result.Scoring is null
                        ? null
                        : new GoldenScoring(
                            result.Scoring.OverallScore,
                            result.Scoring.OverallDirection,
                            result.Scoring.Recommendation,
                            result.Scoring.CategoryScores);

                    cases.Add(new GoldenCase(
                        input.Id,
                        input.Indicators,
                        cfg,
                        new GoldenExpected(result.Indicators, scoring, result.Risk)));
                }

                return cases;
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        private static MarketData MinimalMarketData() => new()
        {
            StockCode = "2330",
            CompanyName = "台積電",
            HistoricalPrices = Enumerable.Range(1, 5).Select(i => new DailyPrice
            {
                Date = new DateTime(2026, 1, i),
                Open = 500 + i, High = 505 + i, Low = 495 + i, Close = 502 + i, Volume = 10000
            }).ToList()
        };

        #endregion

        #region File format

        private static string SerializeGoldenFile(List<GoldenCase> cases)
        {
            var meta = new GoldenMeta(
                Purpose: "TASK-057 的移植黃金資料：現行 C# 評分／風險（StockAnalysisBuilder.BuildAsync 經 FinanceQueryService 的呼叫方式）的輸入與輸出。C# 在 TASK-058 刪除後本檔凍結，前端測試永遠對照它。",
                SourceCommit: SourceCommit,
                ScoringSourcesLastChangedIn: ScoringSourcesLastChangedIn,
                SourceFiles:
                [
                    "JackyAIApp.Server/Services/Finance/Builder/StockAnalysisBuilder.cs",
                    "JackyAIApp.Server/Services/Finance/Scoring/StockScoreService.cs",
                    "JackyAIApp.Server/Services/Finance/Scoring/CategoryWeightConfig.cs",
                ],
                GeneratedBy: "JackyAIApp.Server.Tests/Services/Finance/Scoring/StockScoringGoldenFixtureTests.cs",
                Regenerate: $"{UpdateEnvVar}=1 dotnet test JackyAIApp.Server.Tests --filter FullyQualifiedName~StockScoringGoldenFixtureTests",
                Serialization: "與 API 相同：camelCase、列舉輸出字串（PascalCase 名稱）。decimal 原樣輸出（含 28 位有效數字與尾端 0），不做任何進位；中文以 UTF-8 直接寫入（emoji 等補充平面字元仍是 \\uD83D 這類跳脫，解析結果相同）。",
                Notes:
                [
                    "每個案例：indicators 是 IIndicatorEngine.CalculateAll 的輸出（未過濾）；config 是請求的旗標與三個權重；expected 是 StockAnalysisResult 的 indicators（過濾後、含順序）、scoring（可為 null）、risk（可為 null）。",
                    "scoring 只保留 overallScore／overallDirection／recommendation／categoryScores；categoryScores 順序＝各面向第一次出現的順序。",
                    "id 以 rand- 開頭的是確定性隨機案例（xorshift64，固定種子）；其餘是手工邊界案例，id 說明測的是哪一個地雷。",
                    "includeScoring=false 時 risk 仍會算，但 categoryScores 以空清單傳入，所以不會有「各面向分數差距大」這個風險因子（現行行為，要原樣保留）。",
                ],
                CaseCount: cases.Count,
                HandCraftedCaseCount: cases.Count(c => !c.Id.StartsWith("rand-")),
                RandomCaseCount: cases.Count(c => c.Id.StartsWith("rand-")),
                RandomSeed: "0x" + RandomSeed.ToString("X16"));

            // One case per line: a valid JSON file whose git diff points at the case that changed.
            var sb = new StringBuilder();
            sb.Append("{\n  \"meta\": ").Append(JsonSerializer.Serialize(meta, ApiJson)).Append(",\n  \"cases\": [\n");
            for (var i = 0; i < cases.Count; i++)
            {
                sb.Append("    ").Append(JsonSerializer.Serialize(cases[i], ApiJson));
                sb.Append(i < cases.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        private static string DescribeFirstDifference(string committed, string generated)
        {
            var committedLines = committed.Split('\n');
            var generatedLines = generated.Split('\n');
            var limit = Math.Min(committedLines.Length, generatedLines.Length);
            var index = 0;
            while (index < limit && committedLines[index] == generatedLines[index]) index++;

            var line = index < generatedLines.Length ? generatedLines[index] : "";
            var id = Regex.Match(line, "\"id\":\"([^\"]+)\"");
            var where = id.Success ? $"第一個不同的案例：{id.Groups[1].Value}（檔案第 {index + 1} 行）" : $"第一個不同的位置：檔案第 {index + 1} 行";

            return $"{GoldenRelativePath} 與目前 C# 評分實作的輸出不一致。{where}。\n" +
                   "此檔是 TASK-057 前端移植的對照資料（C# 刪除後凍結）：除非這是有意且已與 Tech Lead 確認的行為變更，否則請還原你對評分邏輯的修改，而不是更新黃金資料。\n" +
                   $"若確實要更新：{UpdateEnvVar}=1 dotnet test JackyAIApp.Server.Tests --filter FullyQualifiedName~StockScoringGoldenFixtureTests，再審閱 git diff。";
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "jackyaiapp.client"))) return dir.FullName;
                dir = dir.Parent;
            }

            Assert.Fail($"找不到 repo 根目錄：從 {AppContext.BaseDirectory} 往上都沒有含 jackyaiapp.client 的資料夾。" +
                        "黃金資料測試必須在完整的 repo checkout 內執行，不能略過。");
            return string.Empty;
        }

        #endregion

        #region Hand-crafted cases

        private const string CategoryGapFactor = "各面向分數差距大，訊號不一致";

        private static List<CaseInput> HandCraftedCases()
        {
            var list = new List<CaseInput>();
            void Add(string id, List<IndicatorResult> indicators, GoldenConfig? config = null) =>
                list.Add(new CaseInput(id, indicators, config ?? DefaultConfig));

            // ---- 結構與順序 ----
            Add("empty-no-indicators", []);
            Add("single-technical", Scores(Technical, "T", 70));
            Add("single-chip", Scores(Chip, "C", 30));
            Add("single-fundamental", Scores(Fundamental, "F", 55));
            Add("order-technical-chip-fundamental",
                [.. Scores(Technical, "T", 70, 60), .. Scores(Chip, "C", 40), .. Scores(Fundamental, "F", 50)]);
            Add("order-fundamental-first",
                [.. Scores(Fundamental, "F", 50), .. Scores(Technical, "T", 70, 60), .. Scores(Chip, "C", 40)]);
            Add("order-interleaved-chip-first",
            [
                Ind("C1", Chip, 40), Ind("T1", Technical, 70), Ind("F1", Fundamental, 50),
                Ind("T2", Technical, 60), Ind("C2", Chip, 45), Ind("F2", Fundamental, 55),
            ]);
            Add("order-interleaved-fundamental-chip-technical",
            [
                Ind("F1", Fundamental, 50), Ind("C1", Chip, 40), Ind("T1", Technical, 70),
                Ind("C2", Chip, 45), Ind("T2", Technical, 60), Ind("F2", Fundamental, 55),
            ]);
            Add("order-chip-then-technical-no-fundamental",
                [.. Scores(Chip, "C", 35, 45), .. Scores(Technical, "T", 65)]);

            // ---- 面向平均 → 方向閾值（用未進位的平均）----
            Add("cat-avg-80-exact-strong-bullish", Scores(Technical, "T", 80, 80, 80));
            Add("cat-avg-79_67-bullish", Scores(Technical, "T", 80, 80, 79));
            Add("cat-avg-60-exact-bullish", Scores(Technical, "T", 60, 60, 60));
            Add("cat-avg-59_67-neutral", Scores(Technical, "T", 60, 60, 59));
            Add("cat-avg-40-exact-neutral", Scores(Technical, "T", 40, 40, 40));
            Add("cat-avg-39_67-bearish", Scores(Technical, "T", 40, 40, 39));
            Add("cat-avg-20-exact-bearish", Scores(Technical, "T", 20, 20, 20));
            Add("cat-avg-19_67-strong-bearish", Scores(Technical, "T", 20, 20, 19));
            Add("cat-all-zero", Scores(Technical, "T", 0, 0, 0));
            Add("cat-all-100", Scores(Technical, "T", 100, 100, 100));
            // 19×base + (base−1) over 20 indicators = base − 0.05: displays as base.0, direction is the one below.
            Add("cat-avg-79_95-shows-80_0-but-bullish", Fill(Technical, "T", 20, 80, 79));
            Add("cat-avg-19_95-shows-20_0-but-strong-bearish", Fill(Technical, "T", 20, 20, 19));
            Add("cat-avg-60_05-half-even-rounds-down", Fill(Technical, "T", 20, 60, 61));

            // ---- Math.Round(decimal, 1) = 銀行家進位 ----
            Add("round-avg-62_25-half-even", Scores(Technical, "T", 62, 62, 62, 63));
            Add("round-avg-62_75-half-even", Scores(Technical, "T", 62, 63, 63, 63));
            Add("round-avg-ties-in-three-categories",
            [
                .. Scores(Technical, "T", 63, 63, 63, 64),     // 63.25 → 63.2
                .. Scores(Chip, "C", 37, 38, 38, 38),          // 37.75 → 37.8
                .. Scores(Fundamental, "F", 44, 44, 44, 45),   // 44.25 → 44.2
            ]);
            // 62.25 × 0.2 = 12.45 → 12.4 (half-even), not 12.5.
            Add("round-weighted-12_45-half-even",
                [.. Scores(Technical, "T", 62, 62, 62, 63), .. Scores(Chip, "C", 50), .. Scores(Fundamental, "F", 50)],
                Weights(0.2m, 0.3m, 0.5m));
            // 62.75 × 0.2 = 12.55 → 12.6 either way; pairs with the case above.
            Add("round-weighted-12_55-half-even",
                [.. Scores(Technical, "T", 62, 63, 63, 63), .. Scores(Chip, "C", 50), .. Scores(Fundamental, "F", 50)],
                Weights(0.2m, 0.3m, 0.5m));

            // ---- 加權總分 = Σ(已進位的各面向加權分)，再判方向 ----
            // Technical 59.9 × 0.5 = 29.95 → 30.0 (half-even), so 30 + 18 + 12 = 60.0 → Bullish; unrounded it is 59.95 → Neutral.
            Add("overall-sum-of-rounded-59_95-is-60",
                [.. Scores(Technical, "T", 60, 60, 60, 60, 60, 60, 60, 60, 60, 59), .. Scores(Chip, "C", 60), .. Scores(Fundamental, "F", 60)]);
            Add("overall-80-exact", OneEach(80, 80, 80));
            Add("overall-79_9", OneEach(80, 80, 79, 80));
            Add("overall-60-exact", OneEach(60, 60, 60));
            Add("overall-59_9", OneEach(60, 60, 59, 60));
            Add("overall-40-exact", OneEach(40, 40, 40));
            Add("overall-39_9", OneEach(40, 40, 39, 40));
            Add("overall-20-exact", OneEach(20, 20, 20));
            Add("overall-19_9", OneEach(20, 20, 19, 20));
            Add("overall-single-category-weight-becomes-1", Scores(Technical, "T", 71, 72, 73));

            // ---- 權重 ----
            var mixed = () => (List<IndicatorResult>)[.. Scores(Technical, "T", 90, 70), .. Scores(Chip, "C", 50), .. Scores(Fundamental, "F", 30, 20)];
            Add("weights-default-50-30-20", mixed());
            Add("weights-reversed-20-30-50", mixed(), Weights(0.2m, 0.3m, 0.5m));
            Add("weights-technical-zero", mixed(), Weights(0m, 0.5m, 0.5m));
            Add("weights-chip-zero", mixed(), Weights(0.5m, 0m, 0.5m));
            Add("weights-fundamental-zero", mixed(), Weights(0.5m, 0.5m, 0m));
            Add("weights-all-zero", mixed(), Weights(0m, 0m, 0m));
            Add("weights-all-zero-single-category", Scores(Technical, "T", 90, 70), Weights(0m, 0m, 0m));
            Add("weights-equal-thirds-from-1-1-1", mixed(), Weights(1m, 1m, 1m));
            Add("weights-sum-1_5", mixed(), Weights(0.8m, 0.4m, 0.3m));
            Add("weights-sum-0_3", mixed(), Weights(0.1m, 0.1m, 0.1m));
            Add("weights-chip-missing-redistributed",
                [.. Scores(Technical, "T", 90, 70), .. Scores(Fundamental, "F", 30, 20)]);
            Add("weights-technical-and-chip-missing",
                Scores(Fundamental, "F", 30, 20));
            // The weights of absent categories do not count: Technical alone with weight 0 → total 0 → no normalization.
            Add("weights-only-present-category-has-zero-weight",
                Scores(Technical, "T", 90, 70), Weights(0m, 0.5m, 0.5m));

            // ---- 旗標 ----
            var allCategories = () => (List<IndicatorResult>)[.. Scores(Technical, "T", 70, 60), .. Scores(Chip, "C", 40, 30), .. Scores(Fundamental, "F", 55, 45)];
            Add("flag-technical-off", allCategories(), DefaultConfig with { IncludeTechnical = false });
            Add("flag-chip-off", allCategories(), DefaultConfig with { IncludeChip = false });
            Add("flag-fundamental-off", allCategories(), DefaultConfig with { IncludeFundamental = false });
            Add("flag-technical-and-chip-off", allCategories(), DefaultConfig with { IncludeTechnical = false, IncludeChip = false });
            Add("flag-all-categories-off", allCategories(),
                DefaultConfig with { IncludeTechnical = false, IncludeChip = false, IncludeFundamental = false });
            Add("flag-filter-leaves-nothing", Scores(Chip, "C", 40, 30), DefaultConfig with { IncludeChip = false });
            Add("flag-scoring-off", allCategories(), DefaultConfig with { IncludeScoring = false });
            Add("flag-risk-off", allCategories(), DefaultConfig with { IncludeRisk = false });
            Add("flag-scoring-off-risk-off", allCategories(), DefaultConfig with { IncludeScoring = false, IncludeRisk = false });

            // Technical 90 vs Chip 10, plus RSI and mixed directions: with scoring off the risk is still computed,
            // but never has the category-gap factor.
            var gapScenario = () => (List<IndicatorResult>)
            [
                Ind("RSI", Technical, 90, value: 85m), Ind("MACD", Technical, 90),
                Ind("MarginIndicator", Chip, 10), Ind("ForeignSell", Chip, 10),
            ];
            Add("flag-scoring-off-risk-on-category-gap-factor-absent", gapScenario(), DefaultConfig with { IncludeScoring = false });
            Add("flag-scoring-on-risk-on-category-gap-factor-present", gapScenario());
            Add("flag-technical-off-drops-rsi-factor",
                [Ind("RSI", Technical, 90, value: 85m), Ind("MarginIndicator", Chip, 50)],
                DefaultConfig with { IncludeTechnical = false });
            Add("flag-chip-off-drops-pledge-factor",
                [Ind("MACD", Technical, 50), Ind("DirectorPledge", Chip, 50, value: 45m)],
                DefaultConfig with { IncludeChip = false });

            // ---- 風險：RSI（> 80、< 20 嚴格；數字用 decimal 的 F1，遇 .x5 往遠離 0 進位）----
            Add("risk-rsi-80-exact-no-factor", [Rsi(80m)]);
            Add("risk-rsi-80_1-overbought", [Rsi(80.1m)]);
            Add("risk-rsi-20-exact-no-factor", [Rsi(20m)]);
            Add("risk-rsi-19_9-oversold", [Rsi(19.9m)]);
            Add("risk-rsi-80_05-f1-rounds-away-from-zero", [Rsi(80.05m)]);
            Add("risk-rsi-80_35-f1-rounds-away-from-zero", [Rsi(80.35m)]);
            Add("risk-rsi-19_95-f1-rounds-up-to-20_0", [Rsi(19.95m)]);
            Add("risk-rsi-100-and-0", [Rsi(100m), Ind("RSI", Technical, 50, value: 0m)]);
            Add("risk-rsi-duplicate-first-wins-neutral-first", [Rsi(50m), Rsi(95m)]);
            Add("risk-rsi-duplicate-first-wins-overbought-first", [Rsi(95m), Rsi(50m)]);
            Add("risk-rsi-name-is-case-sensitive", [Ind("rsi", Technical, 50, value: 95m), Ind("RSI14", Technical, 50, value: 95m)]);

            // ---- 風險：成交量（TodayVsAvg20 > 3）----
            Add("risk-volume-3-exact-no-factor", [Vol(3m)]);
            Add("risk-volume-3_1", [Vol(3.1m)]);
            Add("risk-volume-3_05-f1-rounds-away-from-zero", [Vol(3.05m)]);
            Add("risk-volume-missing-subvalue-no-factor",
                [Ind("VolumeRatio", Technical, 50, value: 9m, sub: Sub(("TodayVsAvg5", 9m)))]);
            Add("risk-volume-subvalue-key-is-case-sensitive",
                [Ind("VolumeRatio", Technical, 50, value: 9m, sub: Sub(("todayVsAvg20", 9m)))]);

            // ---- 風險：董監設質（> 30）----
            Add("risk-pledge-30-exact-no-factor", [Pledge(30m)]);
            Add("risk-pledge-30_1", [Pledge(30.1m)]);
            Add("risk-pledge-30_05-f1-rounds-away-from-zero", [Pledge(30.05m)]);
            Add("risk-pledge-55_55", [Pledge(55.55m)]);

            // ---- 風險：分歧分數 min(多,空) / 總數 × 200（> 30、> 50、> 60 皆嚴格）----
            Add("risk-div-0-all-neutral", DirMix(5, 0, 0, 5));
            Add("risk-div-0-one-sided", DirMix(5, 5, 0, 0));
            Add("risk-div-30-exact-no-factor", DirMix(20, 3, 3, 14));
            Add("risk-div-30_77-moderate", DirMix(13, 2, 2, 9));
            Add("risk-div-50-exact-medium", DirMix(4, 1, 1, 2));
            Add("risk-div-57-high-by-divergence-alone", DirMix(7, 2, 2, 3));
            Add("risk-div-60-exact-moderate-not-severe", DirMix(10, 3, 3, 4));
            Add("risk-div-61_5-severe", DirMix(13, 4, 4, 5));
            Add("risk-div-100-two-indicators", DirMix(2, 1, 1, 0));
            Add("risk-div-strong-directions-are-counted",
            [
                Ind("A", Technical, 95, StrongBullish), Ind("B", Technical, 95, StrongBullish), Ind("C", Technical, 70, Bullish),
                Ind("D", Technical, 50, Neutral),
                Ind("E", Technical, 30, Bearish), Ind("F", Technical, 5, StrongBearish), Ind("G", Technical, 5, StrongBearish),
            ]);

            // ---- 風險：因子個數 → 等級 ----
            Add("risk-factors-0-low", [Rsi(50m), Ind("MACD", Technical, 50)]);
            Add("risk-factors-1-medium", [Rsi(85m, score: 50, direction: Neutral)]);
            Add("risk-factors-2-medium", [Rsi(85m, score: 50, direction: Neutral), Vol(4m)]);
            Add("risk-factors-3-high", [Rsi(85m, score: 50, direction: Neutral), Vol(4m), Pledge(40m, score: 50)]);
            // 4 factors with no divergence at all (all neutral): Technical avg 50 vs Chip 90 supplies the 4th.
            Add("risk-factors-4-very-high",
                [Rsi(85m, score: 50, direction: Neutral), Vol(4m), Pledge(40m, score: 90, direction: Neutral)]);
            // Severe divergence + RSI + volume + pledge + category gap, in the order the code adds them.
            Add("risk-factors-5-all-in-order",
            [
                Rsi(85m, score: 90, direction: Bullish),
                Ind("VolumeRatio", Technical, 10, Bearish, value: 4m, sub: Sub(("TodayVsAvg20", 4m))),
                Pledge(40m, score: 10, direction: Bearish),
            ]);
            // 2 factors only (moderate divergence 57.1 + RSI), but divergence > 50 alone makes it High.
            Add("risk-high-by-divergence-over-50-with-two-factors",
            [
                Rsi(85m, score: 70, direction: Bullish), Ind("A", Technical, 70, Bullish),
                Ind("B", Technical, 30, Bearish), Ind("C", Technical, 30, Bearish),
                Ind("D", Technical, 50, Neutral), Ind("E", Technical, 50, Neutral), Ind("F", Technical, 50, Neutral),
            ]);

            // ---- 風險：各面向分數差距（> 30，用「已進位」的面向分數）----
            Add("gap-30-exact-no-factor", [.. Scores(Technical, "T", 80), .. Scores(Chip, "C", 50)]);
            Add("gap-30_1-factor", [.. Fill(Technical, "T", 10, 80, 81), .. Scores(Chip, "C", 50)]);
            // Technical 80.05 displays as 80.0 (half-even) → gap 30.0, not > 30; the unrounded gap would be 30.05.
            Add("gap-uses-rounded-scores-80_05-vs-50", [.. Fill(Technical, "T", 20, 80, 81), .. Scores(Chip, "C", 50)]);
            Add("gap-single-category-no-factor", Scores(Technical, "T", 95, 5));
            Add("gap-three-categories-uses-max-and-min",
                [.. Scores(Technical, "T", 90), .. Scores(Chip, "C", 50), .. Scores(Fundamental, "F", 55)]);

            // ---- 建議文字 ----
            Add("rec-strong-bullish", Scores(Technical, "T", 90, 85));
            Add("rec-bullish", Scores(Technical, "T", 70, 65));
            Add("rec-neutral", Scores(Technical, "T", 50, 45));
            Add("rec-bearish", Scores(Technical, "T", 30, 25));
            Add("rec-strong-bearish", Scores(Technical, "T", 10, 5));
            Add("rec-technical-bullish-chip-bearish-warning", [.. Scores(Technical, "T", 70), .. Scores(Chip, "C", 30)]);
            Add("rec-technical-bearish-chip-bullish-hint", [.. Scores(Technical, "T", 30), .. Scores(Chip, "C", 70)]);
            Add("rec-technical-strong-bullish-chip-strong-bearish-warning", [.. Scores(Technical, "T", 95), .. Scores(Chip, "C", 5)]);
            Add("rec-technical-strong-bearish-chip-strong-bullish-hint", [.. Scores(Technical, "T", 5), .. Scores(Chip, "C", 95)]);
            Add("rec-technical-bullish-chip-bullish-no-note", [.. Scores(Technical, "T", 70), .. Scores(Chip, "C", 75)]);
            Add("rec-technical-neutral-chip-bearish-no-note", [.. Scores(Technical, "T", 50), .. Scores(Chip, "C", 30)]);
            Add("rec-technical-only-no-note", Scores(Technical, "T", 70));
            Add("rec-chip-only-no-note", Scores(Chip, "C", 30));
            Add("rec-fundamental-is-ignored-by-the-notes", [.. Scores(Technical, "T", 70), .. Scores(Fundamental, "F", 30)]);
            // Overall can be StrongBearish (all weights zero) while the notes still follow the category directions.
            Add("rec-note-follows-category-directions-not-overall",
                [.. Scores(Technical, "T", 70), .. Scores(Chip, "C", 30)], Weights(0m, 0m, 0m));

            // ---- 方向值、特殊字元、極端數值 ----
            Add("directions-all-five-in-one-category",
            [
                Ind("A", Technical, 90, StrongBullish), Ind("B", Technical, 70, Bullish), Ind("C", Technical, 50, Neutral),
                Ind("D", Technical, 30, Bearish), Ind("E", Technical, 10, StrongBearish), Ind("F", Technical, 50, Neutral),
            ]);
            Add("special-characters-and-numbers",
            [
                Ind("RSI", Technical, 66, Bullish, value: 61.5m, signal: "偏多 \"強勢\" ⚠️💡", reason: "RSI=61.5，高於 50；<b>&amp;</b>\\n\t換行與跳脫"),
                Ind("MACD", Technical, 58, Bullish, value: -0.1234567890123456789m, sub: Sub(("Signal", -0.5m), ("Histogram", 0.3750m), ("Macd", 1.50m))),
                Ind("營收年增率", Fundamental, 72, Bullish, value: 123456789012345.6789m, signal: "ｆｕｌｌｗｉｄｔｈ", reason: "含全形字元與 emoji 😀 以及 Ünïcödé"),
                Ind("MarginIndicator", Chip, 33, Bearish, value: 0.1m + 0.2m, reason: "line1\nline2\ttab \\ backslash"),
            ]);
            Add("score-out-of-range-high", Scores(Technical, "T", 150, 120));
            Add("score-out-of-range-low", Scores(Technical, "T", -20, -50));
            Add("duplicate-indicator-names-across-categories",
                [Ind("Same", Technical, 80), Ind("Same", Chip, 20), Ind("Same", Fundamental, 50), Ind("Same", Technical, 60)]);

            return list;
        }

        /// <summary>One indicator each for Technical, Chip, Fundamental (and optionally a second Fundamental).</summary>
        private static List<IndicatorResult> OneEach(params int[] scores)
        {
            var result = new List<IndicatorResult>
            {
                Ind("T1", Technical, scores[0]),
                Ind("C1", Chip, scores[1]),
            };
            for (var i = 2; i < scores.Length; i++)
                result.Add(Ind($"F{i - 1}", Fundamental, scores[i]));
            return result;
        }

        #endregion

        #region Random cases

        private static List<CaseInput> RandomCases()
        {
            // Own generator: System.Random's sequence is not guaranteed to be the same across .NET versions.
            var rng = new Xorshift64(RandomSeed);

            string[] technicalNames = ["RSI", "MACD", "KD", "MA", "VolumeRatio", "Bollinger"];
            string[] chipNames = ["MarginIndicator", "ForeignBuy", "DirectorPledge", "ShortSell"];
            string[] fundamentalNames = ["PE", "PB", "DividendYield", "EPSGrowth"];

            var cases = new List<CaseInput>(RandomCaseCount);
            for (var n = 1; n <= RandomCaseCount; n++)
            {
                // 0–30 indicators. Short lists make ties like 62.25 likely and are cheap in the file;
                // the long ones (30%) keep the weighted sums busy.
                var count = rng.Next(100) < 70 ? rng.Next(9) : 9 + rng.Next(22);

                // 60% of cases draw from all three categories, the rest from one or two. Categories are
                // taken in a random order and each gets an indicator first, so the first-appearance order
                // (which fixes the order of categoryScores) varies and no chosen category ends up empty.
                var categories = new List<IndicatorCategory> { Technical, Chip, Fundamental };
                for (var i = categories.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (categories[i], categories[j]) = (categories[j], categories[i]);
                }
                var wanted = rng.Next(100) < 60 ? 3 : 1 + rng.Next(2);
                categories = categories.Take(Math.Max(1, Math.Min(wanted, count))).ToList();
                var centers = new Dictionary<IndicatorCategory, int>();
                foreach (var category in categories) centers[category] = 5 + rng.Next(91);

                var indicators = new List<IndicatorResult>(count);
                for (var i = 0; i < count; i++)
                {
                    var category = i < categories.Count ? categories[i] : categories[rng.Next(categories.Count)];
                    var names = category switch { Technical => technicalNames, Chip => chipNames, _ => fundamentalNames };
                    var name = names[rng.Next(names.Length)];
                    var score = Math.Clamp(centers[category] + rng.Next(41) - 20, 0, 100);
                    var direction = rng.Next(100) < 70 ? DirectionFor(score) : (SignalDirection)rng.Next(5);

                    decimal value;
                    Dictionary<string, decimal>? sub = null;
                    switch (name)
                    {
                        case "RSI": value = rng.Next(10001) / 100m; break;
                        case "DirectorPledge": value = rng.Next(6001) / 100m; break;
                        case "VolumeRatio":
                            value = rng.Next(501) / 100m;
                            if (rng.Next(100) < 80) sub = Sub(("TodayVsAvg20", rng.Next(601) / 100m));
                            break;
                        default: value = (rng.Next(20001) - 10000) / 100m; break;
                    }

                    indicators.Add(Ind(name, category, score, direction, value, signal: "", sub: sub));
                }

                var config = new GoldenConfig(
                    IncludeTechnical: rng.Next(100) < 88,
                    IncludeChip: rng.Next(100) < 88,
                    IncludeFundamental: rng.Next(100) < 88,
                    IncludeScoring: rng.Next(100) < 85,
                    IncludeRisk: rng.Next(100) < 85,
                    TechnicalWeight: rng.Next(21) * 5 / 100m,
                    ChipWeight: rng.Next(21) * 5 / 100m,
                    FundamentalWeight: rng.Next(21) * 5 / 100m);

                cases.Add(new CaseInput($"rand-{n:000}", indicators, config));
            }

            return cases;
        }

        private sealed class Xorshift64(ulong seed)
        {
            private ulong _state = seed;

            private ulong NextUInt64()
            {
                _state ^= _state << 13;
                _state ^= _state >> 7;
                _state ^= _state << 17;
                return _state;
            }

            /// <summary>Uniform-ish integer in [0, maxExclusive).</summary>
            public int Next(int maxExclusive) => (int)(NextUInt64() % (ulong)maxExclusive);
        }

        #endregion

        #region Input helpers

        /// <summary>Test-input convenience only: gives a made-up indicator a plausible direction for its score.</summary>
        private static SignalDirection DirectionFor(int score) => score switch
        {
            >= 80 => StrongBullish,
            >= 60 => Bullish,
            >= 40 => Neutral,
            >= 20 => Bearish,
            _ => StrongBearish,
        };

        private static string SignalFor(SignalDirection direction) => direction switch
        {
            StrongBullish => "強多",
            Bullish => "偏多",
            Neutral => "中性",
            Bearish => "偏空",
            _ => "強空",
        };

        private static IndicatorResult Ind(
            string name,
            IndicatorCategory category,
            int score,
            SignalDirection? direction = null,
            decimal value = 0m,
            string? signal = null,
            string reason = "",
            Dictionary<string, decimal>? sub = null)
        {
            var effective = direction ?? DirectionFor(score);
            return new IndicatorResult
            {
                Name = name,
                Category = category,
                Value = value,
                SubValues = sub ?? new Dictionary<string, decimal>(),
                Signal = signal ?? SignalFor(effective),
                Direction = effective,
                Score = score,
                Reason = reason,
            };
        }

        private static Dictionary<string, decimal> Sub(params (string Key, decimal Value)[] items) =>
            items.ToDictionary(i => i.Key, i => i.Value);

        private static List<IndicatorResult> Scores(IndicatorCategory category, string prefix, params int[] scores) =>
            scores.Select((s, i) => Ind($"{prefix}{i + 1}", category, s)).ToList();

        /// <summary>count indicators: count−1 at baseScore and the last at lastScore.</summary>
        private static List<IndicatorResult> Fill(IndicatorCategory category, string prefix, int count, int baseScore, int lastScore) =>
            Enumerable.Range(1, count).Select(i => Ind($"{prefix}{i}", category, i == count ? lastScore : baseScore)).ToList();

        private static IndicatorResult Rsi(decimal value, int score = 50, SignalDirection? direction = Neutral) =>
            Ind("RSI", Technical, score, direction, value);

        private static IndicatorResult Vol(decimal todayVsAvg20, int score = 50) =>
            Ind("VolumeRatio", Technical, score, Neutral, value: 1.2m, sub: Sub(("TodayVsAvg20", todayVsAvg20), ("TodayVsAvg5", 1.1m)));

        private static IndicatorResult Pledge(decimal value, int score = 50, SignalDirection? direction = Neutral) =>
            Ind("DirectorPledge", Chip, score, direction, value);

        /// <summary>total indicators of one category: bullish first, then bearish, the rest neutral.</summary>
        private static List<IndicatorResult> DirMix(int total, int bullish, int bearish, int neutral)
        {
            Assert.Equal(total, bullish + bearish + neutral);
            var result = new List<IndicatorResult>();
            for (var i = 0; i < bullish; i++) result.Add(Ind($"B{i + 1}", Technical, 70, Bullish));
            for (var i = 0; i < bearish; i++) result.Add(Ind($"S{i + 1}", Technical, 30, Bearish));
            for (var i = 0; i < neutral; i++) result.Add(Ind($"N{i + 1}", Technical, 50, Neutral));
            return result;
        }

        private static GoldenConfig Weights(decimal technical, decimal chip, decimal fundamental) =>
            DefaultConfig with { TechnicalWeight = technical, ChipWeight = chip, FundamentalWeight = fundamental };

        #endregion

        #region Assertion helpers

        private static CategoryScore Cat(GoldenCase golden, IndicatorCategory category) =>
            golden.Expected.Scoring!.CategoryScores.Single(c => c.Category == category);

        private static void AssertOverall(Dictionary<string, GoldenCase> byId, string id, decimal score, SignalDirection direction)
        {
            var scoring = byId[id].Expected.Scoring!;
            Assert.Equal(score, scoring.OverallScore);
            Assert.Equal(direction, scoring.OverallDirection);
        }

        private static void AssertDivergence(Dictionary<string, GoldenCase> byId, string id, decimal divergence, RiskLevel level, int factors)
        {
            var risk = byId[id].Expected.Risk!;
            Assert.Equal(divergence, risk.DivergenceScore);
            Assert.Equal(level, risk.Level);
            Assert.Equal(factors, risk.Factors.Count);
        }

        #endregion

        #region Types

        private sealed record CaseInput(string Id, List<IndicatorResult> Indicators, GoldenConfig Config);

        private sealed record GoldenConfig(
            bool IncludeTechnical, bool IncludeChip, bool IncludeFundamental,
            bool IncludeScoring, bool IncludeRisk,
            decimal TechnicalWeight, decimal ChipWeight, decimal FundamentalWeight);

        private sealed record GoldenScoring(
            decimal OverallScore, SignalDirection OverallDirection, string Recommendation, List<CategoryScore> CategoryScores);

        private sealed record GoldenExpected(List<IndicatorResult> Indicators, GoldenScoring? Scoring, RiskAssessment? Risk);

        private sealed record GoldenCase(string Id, List<IndicatorResult> Indicators, GoldenConfig Config, GoldenExpected Expected);

        private sealed record GoldenMeta(
            string Purpose, string SourceCommit, string ScoringSourcesLastChangedIn, string[] SourceFiles,
            string GeneratedBy, string Regenerate, string Serialization, string[] Notes,
            int CaseCount, int HandCraftedCaseCount, int RandomCaseCount, string RandomSeed);

        #endregion
    }
}
