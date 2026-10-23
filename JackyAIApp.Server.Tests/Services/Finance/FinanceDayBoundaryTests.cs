using System.Security.Claims;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Where the Finance page's days turn. What is "today's" for a reader in Taiwan — the market
    /// digest, an indicator's insight, the macro data — turns at midnight in Taipei, which is still
    /// the day before in UTC (the server's clock). The digest's charge follows the usage budget's
    /// day instead, which is the UTC day.
    /// </summary>
    public class FinanceDayBoundaryTests
    {
        private static readonly DateOnly Oct22 = new(2026, 10, 22), Oct23 = new(2026, 10, 23);

        private readonly TestClock _clock = new(TestClock.Taipei(Oct22, 14));
        private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private readonly Mock<IUsageLimitService> _usage = new();
        private readonly Mock<IOpenAIPromptService> _openAI = new();
        private readonly Mock<IMarketDataQueryService> _marketData = new();

        public FinanceDayBoundaryTests()
        {
            _usage.Setup(u => u.TryConsumeAsync("u1", UsageFeature.MarketDigest, It.IsAny<ulong>())).ReturnsAsync(true);
            _marketData.Setup(m => m.GetMarketOverviewAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                ServiceResult.Ok(new MarketOverviewResponse { Quotes = [new MarketQuote { Name = "加權指數", Price = 23000m }] }));
            _openAI.Setup(o => o.GetCompletionWithSystemPromptAsync<MarketDigestResponse>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<IEnumerable<(string, string)>?>(), It.IsAny<string?>()))
                .ReturnsAsync(() => (new MarketDigestResponse { Points = [new DigestPoint { Headline = "台股收高" }] }, null));
        }

        private MarketInsightService Insights()
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "Cookies")),
            };
            return new MarketInsightService(
                NullLogger<MarketInsightService>.Instance, TestCurrentUser.Of(http), _cache, _usage.Object,
                Mock.Of<IFinnhubNewsProvider>(), Mock.Of<ITwseOpenApi>(), _openAI.Object, _marketData.Object, _clock);
        }

        private int DigestsGenerated() => _openAI.Invocations.Count(i => i.Method.Name == "GetCompletionWithSystemPromptAsync");

        private int Charges() => _usage.Invocations.Count(i => i.Method.Name == nameof(IUsageLimitService.TryConsumeAsync));

        [Fact]
        public async Task TheDigest_IsNewAtMidnightInTaipei_ButChargedOncePerUtcDay()
        {
            var service = Insights();

            _clock.Now = TestClock.Taipei(Oct22, 23, 50);
            await service.GetMarketDigestAsync(default);
            Assert.Equal((1, 1), (DigestsGenerated(), Charges()));

            // Past midnight in Taipei, still the 22nd in UTC: the 23rd's digest, on the budget day already paid.
            _clock.Now = TestClock.Taipei(Oct23, 0, 10);
            await service.GetMarketDigestAsync(default);
            Assert.Equal((2, 1), (DigestsGenerated(), Charges()));

            // 08:10 in Taipei is the 23rd in UTC too: the same digest, on a new budget day.
            _clock.Now = TestClock.Taipei(Oct23, 8, 10);
            await service.GetMarketDigestAsync(default);
            Assert.Equal((2, 2), (DigestsGenerated(), Charges()));
        }

        [Fact]
        public async Task AnIndicatorsInsight_IsNewAtMidnightInTaipei()
        {
            _marketData.Setup(m => m.GetIndicatorsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(
                new IndicatorExplorerResponse { Indicators = [new IndicatorSeries { Id = "us10y", Name = "美國10年期公債殖利率" }] }));
            _openAI.Setup(o => o.GetCompletionWithSystemPromptAsync<IndicatorInsight>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<IEnumerable<(string, string)>?>(), It.IsAny<string?>()))
                .ReturnsAsync(() => (new IndicatorInsight { Insight = "殖利率走升" }, null));
            var service = Insights();

            _clock.Now = TestClock.Taipei(Oct22, 23, 50);
            var lateEvening = await service.GetIndicatorInsightAsync("us10y", default);
            _clock.Now = TestClock.Taipei(Oct23, 0, 10);
            var pastMidnight = await service.GetIndicatorInsightAsync("us10y", default);

            Assert.NotSame(lateEvening.Data, pastMidnight.Data);
        }

        [Fact]
        public async Task TheMacroData_IsFetchedAgainAtMidnightInTaipei()
        {
            var macro = new Mock<IMacroEconomyProvider>();
            macro.Setup(m => m.FetchAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new MacroEconomyResponse());
            var service = new MarketDataQueryService(
                NullLogger<MarketDataQueryService>.Instance, _cache, macro.Object, Mock.Of<IMarketOverviewProvider>(),
                Mock.Of<IMarketIndexHistoryProvider>(), Mock.Of<IIndicatorProvider>(), _clock);

            _clock.Now = TestClock.Taipei(Oct22, 23, 50);
            await service.GetMacroEconomyAsync(default);
            _clock.Now = TestClock.Taipei(Oct23, 0, 10);
            await service.GetMacroEconomyAsync(default);

            macro.Verify(m => m.FetchAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }
}
