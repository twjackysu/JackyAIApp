using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.Finance.DataProviders;
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
    /// about: set those, and everything else is an empty mock (or, for the optional ones, absent, as
    /// before). The one place to change when their constructors do.
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
        public TimeProvider? Time { get; init; }
        public ITdccFxService? TdccFx { get; init; }
        public ICathayFxService? CathayFx { get; init; }
        public ServerTiming? Timing { get; init; }
        public IDeepHistoryService? DeepHistory { get; init; }
        public ITradingCalendar? Calendar { get; init; }

        public PortfolioPriceLoader BuildPriceLoader() =>
            new(Providers, FundData, HttpClientFactory, Cache, NullLogger<PortfolioPriceLoader>.Instance);

        /// <summary>Over a factory handing out contexts on <paramref name="db"/>'s options: with an
        /// InMemory database they see the same data as the test's own context.</summary>
        public HoldingSnapshotReader BuildSnapshotReader() =>
            new(new TestDbContextFactory((DbContextOptions<AzureSQLDBContext>)db.GetService<IDbContextOptions>()), Cache);

        public PortfolioService Build() =>
            new(db, BuildSnapshotReader(), BuildPriceLoader(), Dividends, Categories,
                NullLogger<PortfolioService>.Instance,
                timeProvider: Time, tdccFx: TdccFx, cathayFx: CathayFx, timing: Timing,
                deepHistory: DeepHistory, tradingCalendar: Calendar);

        public HoldingSearchService BuildSearch() =>
            new(TwListedCompanies, FundData, HttpClientFactory, NullLogger<HoldingSearchService>.Instance);

        public HoldingPriceService BuildPrices() => new(BuildSnapshotReader(), BuildPriceLoader(), FundData);

        public HoldingAiSummaryService BuildAiSummary() =>
            new(BuildSnapshotReader(), BuildPriceLoader(), FundData, OpenAI);
    }
}
