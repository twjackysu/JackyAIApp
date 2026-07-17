using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TWStockLib.Models;
using TWStockLib.Services;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// Covers the DB-backed TW stock search added to fix "查無股票" firing on every symbol whenever
    /// TWSE's isin.twse.com.tw lookup has an outage/maintenance window — the holdings editor's
    /// autocomplete used to call TWStockLib's GetAllStockListAsync live on every keystroke with no
    /// fallback. Now it's served from TwListedCompanies, refreshed at most once per 24h, with a
    /// failed refresh falling back to whatever's already in the table instead of an empty result.
    /// </summary>
    public class PortfolioStockSearchTests : IDisposable
    {
        private const string TestUserId = "search-test-user";

        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<IStockMarketService> _stockService;
        private readonly PortfolioService _service;

        public PortfolioStockSearchTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            var providerFactory = new Mock<IMarketDataProviderFactory>();
            var fundData = new Mock<IFundDataService>();
            var dividendData = new Mock<IDividendService>();
            var httpClientFactory = new Mock<IHttpClientFactory>();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var openAI = new Mock<IOpenAIPromptService>();
            var creditService = new Mock<ICreditService>();
            var categoryService = new Mock<IStockCategoryService>();
            _stockService = new Mock<IStockMarketService>();

            _service = new PortfolioService(
                _dbContext,
                providerFactory.Object,
                fundData.Object,
                dividendData.Object,
                categoryService.Object,
                _stockService.Object,
                httpClientFactory.Object,
                cache,
                openAI.Object,
                creditService.Object,
                NullLogger<PortfolioService>.Instance);
        }

        public void Dispose() => _dbContext.Dispose();

        private void SetupLiveList(bool success, params (string Symbol, string Name)[] companies)
        {
            var result = success
                ? StockResult<IReadOnlyDictionary<string, StockData>>.Ok(
                    companies.ToDictionary(
                        c => c.Symbol,
                        c => new StockData { Symbol = c.Symbol, Name = c.Name, Market = MarketType.TSE }))
                : StockResult<IReadOnlyDictionary<string, StockData>>.Fail("UpstreamError", "isin.twse.com.tw unavailable");
            _stockService
                .Setup(x => x.GetAllStockListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
        }

        [Fact]
        public async Task SearchStocksAsync_EmptyDb_FetchesLiveAndPersistsWholeList()
        {
            SetupLiveList(true, ("2330", "台積電"), ("2317", "鴻海"));

            var result = await _service.SearchStocksAsync("TW", "2330", CancellationToken.None);

            var hit = Assert.Single(result);
            Assert.Equal("2330", hit.Symbol);
            Assert.Equal("台積電", hit.Name);

            var persisted = await _dbContext.TwListedCompanies.ToListAsync();
            Assert.Equal(2, persisted.Count); // both companies from the live fetch, not just the match
        }

        [Fact]
        public async Task SearchStocksAsync_DbAlreadyFresh_DoesNotCallLiveSource()
        {
            _dbContext.TwListedCompanies.Add(new TwListedCompany
            {
                Symbol = "2330", Name = "台積電", LastRefreshedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();

            var result = await _service.SearchStocksAsync("TW", "2330", CancellationToken.None);

            Assert.Single(result);
            _stockService.Verify(
                x => x.GetAllStockListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SearchStocksAsync_DbStale_RefreshesAndOverwritesWholeTable()
        {
            _dbContext.TwListedCompanies.Add(new TwListedCompany
            {
                // Symbol later dropped from the live list — the overwrite must remove it, not just add.
                Symbol = "0000", Name = "已下市舊資料", LastRefreshedAt = DateTime.UtcNow.AddHours(-25),
            });
            await _dbContext.SaveChangesAsync();
            SetupLiveList(true, ("2330", "台積電"));

            var result = await _service.SearchStocksAsync("TW", "台積電", CancellationToken.None);

            Assert.Single(result);
            var persisted = await _dbContext.TwListedCompanies.ToListAsync();
            var row = Assert.Single(persisted);
            Assert.Equal("2330", row.Symbol);
        }

        [Fact]
        public async Task SearchStocksAsync_StaleDbButLiveFetchFails_FallsBackToExistingRowsInsteadOfEmpty()
        {
            _dbContext.TwListedCompanies.Add(new TwListedCompany
            {
                Symbol = "2330", Name = "台積電", LastRefreshedAt = DateTime.UtcNow.AddHours(-25),
            });
            await _dbContext.SaveChangesAsync();
            SetupLiveList(false);

            var result = await _service.SearchStocksAsync("TW", "2330", CancellationToken.None);

            // Regression guard for the exact bug reported: an upstream outage must not turn into
            // "查無股票" for a symbol the DB already knows about.
            var hit = Assert.Single(result);
            Assert.Equal("2330", hit.Symbol);
        }

        [Fact]
        public async Task SearchStocksAsync_EmptyDbAndLiveFetchFails_ReturnsEmptyWithoutThrowing()
        {
            SetupLiveList(false);

            var result = await _service.SearchStocksAsync("TW", "2330", CancellationToken.None);

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchStocksAsync_MatchesByNameToo_NotJustSymbolPrefix()
        {
            _dbContext.TwListedCompanies.Add(new TwListedCompany
            {
                Symbol = "2330", Name = "台積電", LastRefreshedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();

            var result = await _service.SearchStocksAsync("TW", "台積電", CancellationToken.None);

            Assert.Single(result);
        }
    }
}
