using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// PortfolioService's half of TW stock search: it hands TW keywords to the listed-company
    /// list and does nothing else on the way. The list itself (refresh, matching, caching) is
    /// covered by <see cref="TwListedCompanyServiceTests"/>.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioStockSearchTests : IDisposable
    {
        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<ITwListedCompanyService> _twList = new();
        private readonly PortfolioService _service;

        public PortfolioStockSearchTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _service = new PortfolioService(
                _dbContext,
                new Mock<IMarketDataProviderFactory>().Object,
                new Mock<IFundDataService>().Object,
                new Mock<IDividendService>().Object,
                new Mock<IStockCategoryService>().Object,
                _twList.Object,
                new Mock<IHttpClientFactory>().Object,
                new MemoryCache(new MemoryCacheOptions()),
                new Mock<IOpenAIPromptService>().Object,
                new Mock<IUsageLimitService>().Object,
                NullLogger<PortfolioService>.Instance);
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task SearchStocksAsync_TW_ReturnsTheListsMatches()
        {
            var hits = new List<StockSearchItem> { new() { Symbol = "00981A", Name = "主動統一台股增長", Market = "TW" } };
            _twList.Setup(x => x.SearchAsync("00981A", It.IsAny<CancellationToken>())).ReturnsAsync(hits);

            var result = await _service.SearchStocksAsync("TW", " 00981A ", CancellationToken.None);

            Assert.Same(hits, result);
        }

        [Fact]
        public async Task SearchStocksAsync_TW_NeverRefreshesTheList()
        {
            // Refreshing inside the request is what made every search wait 30s.
            _twList.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

            await _service.SearchStocksAsync("TW", "2330", CancellationToken.None);

            _twList.Verify(x => x.RefreshAsync(It.IsAny<CancellationToken>()), Times.Never);
            _twList.Verify(x => x.GetLastRefreshedAtAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SearchStocksAsync_BlankKeyword_DoesNotSearch()
        {
            var result = await _service.SearchStocksAsync("TW", "   ", CancellationToken.None);

            Assert.Empty(result);
            _twList.VerifyNoOtherCalls();
        }
    }
}
