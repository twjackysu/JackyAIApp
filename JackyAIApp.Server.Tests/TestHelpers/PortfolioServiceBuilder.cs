using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// Builds <see cref="PortfolioService"/>, and the services split out of it, from what a test cares
    /// about: set those, and everything else is an empty mock, or a source with nothing to say
    /// (<see cref="AbsentPortfolioSources"/>). The one place to change when their constructors do.
    /// </summary>
    internal sealed class PortfolioServiceBuilder(AzureSQLDBContext db)
    {
        public IMarketDataProviderFactory Providers { get; init; } = new Mock<IMarketDataProviderFactory>().Object;
        public IFundDataService FundData { get; init; } = new Mock<IFundDataService>().Object;
        public IDividendService Dividends { get; init; } = new Mock<IDividendService>().Object;
        public IStockCategoryService Categories { get; init; } = new Mock<IStockCategoryService>().Object;
        public ITwListedCompanyService TwListedCompanies { get; init; } = new Mock<ITwListedCompanyService>().Object;
        public IHttpClientFactory HttpClientFactory { get; init; } = new Mock<IHttpClientFactory>().Object;
        public IMemoryCache Cache { get; init; } = new MemoryCache(new MemoryCacheOptions());
        public IOpenAIPromptService OpenAI { get; init; } = new Mock<IOpenAIPromptService>().Object;
        public TimeProvider Time { get; init; } = TimeProvider.System;
        public ITdccFxService TdccFx { get; init; } = AbsentPortfolioSources.TdccFx;
        public ICathayFxService CathayFx { get; init; } = AbsentPortfolioSources.CathayFx;
        public ServerTiming Timing { get; init; } = new();
        public IDeepHistoryService DeepHistory { get; init; } = AbsentPortfolioSources.DeepHistory;
        public ITwQuotePrefetcher TwQuotes { get; init; } = AbsentPortfolioSources.TwQuotes;
        public ITradingCalendar Calendar { get; init; } = WeekdayTradingCalendar.Instance;

        public PortfolioPriceLoader BuildPriceLoader() =>
            new(Providers, TwQuotes, FundData, HttpClientFactory, Cache, NullLogger<PortfolioPriceLoader>.Instance);

        /// <summary>Over a factory handing out contexts on <paramref name="db"/>'s options: with an
        /// InMemory database they see the same data as the test's own context.</summary>
        public HoldingSnapshotReader BuildSnapshotReader() =>
            new(new TestDbContextFactory((DbContextOptions<AzureSQLDBContext>)db.GetService<IDbContextOptions>()), Cache);

        public PortfolioService Build() =>
            new(db, BuildSnapshotReader(), BuildPriceLoader(), Dividends,
                timeProvider: Time, tdccFx: TdccFx, cathayFx: CathayFx, timing: Timing,
                tradingCalendar: Calendar);

        public PortfolioHistoryExtender BuildHistoryExtender() =>
            new(NullLogger<PortfolioHistoryExtender>.Instance, Time, DeepHistory);

        public NetWorthHistoryService BuildNetWorthHistory() =>
            new(BuildSnapshotReader(), BuildPriceLoader(), Categories, Time);

        public DrawdownAnalysisService BuildDrawdown() =>
            new(BuildSnapshotReader(), BuildPriceLoader(), BuildHistoryExtender(), Time);

        public BenchmarkComparisonService BuildBenchmark() =>
            new(BuildSnapshotReader(), BuildPriceLoader(), Dividends, BuildHistoryExtender(), Time);

        public HoldingSearchService BuildSearch() =>
            new(TwListedCompanies, FundData, HttpClientFactory, NullLogger<HoldingSearchService>.Instance);

        public HoldingPriceService BuildPrices() => new(BuildSnapshotReader(), BuildPriceLoader(), FundData);

        public HoldingAiSummaryService BuildAiSummary() =>
            new(BuildSnapshotReader(), BuildPriceLoader(), FundData, OpenAI);
    }
}
