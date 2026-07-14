using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// StockCategoryService lazily classifies each held symbol into a fixed PortfolioCategory via
    /// AI, caches the result in StockSymbolInfo forever (same DB-cache pattern as
    /// PersistentTwHistoricalProvider's market-type resolution), and never trusts the model's
    /// category string verbatim — anything outside the enum must fall back to Other. See
    /// .claude/plans/portfolio-industry-classification.md.
    /// </summary>
    public class StockCategoryServiceTests : IDisposable
    {
        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<IOpenAIPromptService> _promptService;
        private readonly Mock<ITWSEOpenAPIService> _twseOpenApi;
        private readonly StockCategoryService _service;

        public StockCategoryServiceTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _promptService = new Mock<IOpenAIPromptService>();
            _twseOpenApi = new Mock<ITWSEOpenAPIService>();
            _twseOpenApi.Setup(x => x.GetCompanyProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("");

            _service = new StockCategoryService(
                new TestDbContextFactory(options), _promptService.Object, _twseOpenApi.Object,
                NullLogger<StockCategoryService>.Instance);
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GetCategoryAsync_Cash_ReturnsCashWithoutAiOrDbWrite()
        {
            var (category, subCategory) = await _service.GetCategoryAsync(AssetType.CASH, "", "現金", "");

            Assert.Equal(PortfolioCategory.Cash, category);
            Assert.Null(subCategory);
            _promptService.Verify(x => x.GetCompletionAsync<CategoryClassificationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>(), It.IsAny<float?>()), Times.Never);
            Assert.Empty(await _dbContext.StockSymbolInfos.ToListAsync());
        }

        [Fact]
        public async Task GetCategoryAsync_Liability_ReturnsLiabilityWithoutAiOrDbWrite()
        {
            var (category, subCategory) = await _service.GetCategoryAsync(AssetType.LIABILITY, "", "信貸", "");

            Assert.Equal(PortfolioCategory.Liability, category);
            Assert.Null(subCategory);
            _promptService.Verify(x => x.GetCompletionAsync<CategoryClassificationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>(), It.IsAny<float?>()), Times.Never);
        }

        [Fact]
        public async Task GetCategoryAsync_UncachedSymbol_CallsAiAndPersists()
        {
            _promptService.Setup(x => x.GetCompletionAsync<CategoryClassificationResult>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>(), It.IsAny<float?>()))
                .ReturnsAsync((new CategoryClassificationResult
                {
                    Category = nameof(PortfolioCategory.Semiconductor),
                    SubCategory = "晶圓代工",
                }, (string?)null));

            var (category, subCategory) = await _service.GetCategoryAsync(AssetType.STOCK, "2330", "台積電", "TW");

            Assert.Equal(PortfolioCategory.Semiconductor, category);
            Assert.Equal("晶圓代工", subCategory);

            var persisted = await _dbContext.StockSymbolInfos.SingleAsync(s => s.Symbol == "2330");
            Assert.Equal(nameof(PortfolioCategory.Semiconductor), persisted.Category);
            Assert.Equal("晶圓代工", persisted.SubCategory);
            Assert.NotNull(persisted.ClassifiedAt);
        }

        [Fact]
        public async Task GetCategoryAsync_AlreadyClassifiedSymbol_ReturnsFromDbWithoutCallingAi()
        {
            _dbContext.StockSymbolInfos.Add(new StockSymbolInfo
            {
                Symbol = "2330", Market = "TW",
                Category = nameof(PortfolioCategory.Semiconductor), SubCategory = "晶圓代工",
                ClassifiedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();

            var (category, subCategory) = await _service.GetCategoryAsync(AssetType.STOCK, "2330", "台積電", "TW");

            Assert.Equal(PortfolioCategory.Semiconductor, category);
            Assert.Equal("晶圓代工", subCategory);
            _promptService.Verify(x => x.GetCompletionAsync<CategoryClassificationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>(), It.IsAny<float?>()), Times.Never);
        }

        [Fact]
        public async Task GetCategoryAsync_AiReturnsUnrecognizedCategory_FallsBackToOther()
        {
            _promptService.Setup(x => x.GetCompletionAsync<CategoryClassificationResult>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>(), It.IsAny<float?>()))
                .ReturnsAsync((new CategoryClassificationResult
                {
                    Category = "TotallyMadeUpCategory",
                    SubCategory = "???",
                }, (string?)null));

            var (category, _) = await _service.GetCategoryAsync(AssetType.STOCK, "9999", "Mystery Corp", "US");

            Assert.Equal(PortfolioCategory.Other, category);
        }

        [Fact]
        public async Task GetCategoryAsync_AiCallFails_FallsBackToOtherWithoutThrowing()
        {
            _promptService.Setup(x => x.GetCompletionAsync<CategoryClassificationResult>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>(), It.IsAny<float?>()))
                .ReturnsAsync(((CategoryClassificationResult?)null, "OpenAI error"));

            var (category, subCategory) = await _service.GetCategoryAsync(AssetType.STOCK, "AAPL", "Apple", "US");

            Assert.Equal(PortfolioCategory.Other, category);
            Assert.Null(subCategory);
        }

        [Fact]
        public async Task GetCategoryAsync_ConcurrentlyForManySymbols_DoesNotThrow()
        {
            // Regression guard for the exact DbContext-concurrency bug that hit
            // PersistentTwHistoricalProvider in prod (PR #209): PortfolioService fans
            // classification out across every distinct held symbol via Task.WhenAll.
            _promptService.Setup(x => x.GetCompletionAsync<CategoryClassificationResult>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>(), It.IsAny<float?>()))
                .ReturnsAsync((new CategoryClassificationResult
                {
                    Category = nameof(PortfolioCategory.TechSoftware),
                    SubCategory = "軟體服務",
                }, (string?)null));

            var symbols = Enumerable.Range(1, 20).Select(i => $"S{i:0000}").ToList();

            var results = await Task.WhenAll(
                symbols.Select(s => _service.GetCategoryAsync(AssetType.STOCK, s, "Test " + s, "US")));

            Assert.All(results, r => Assert.Equal(PortfolioCategory.TechSoftware, r.Category));
            var persistedCount = await _dbContext.StockSymbolInfos.CountAsync();
            Assert.Equal(symbols.Count, persistedCount);
        }

        private class TestDbContextFactory(DbContextOptions<AzureSQLDBContext> options)
            : IDbContextFactory<AzureSQLDBContext>
        {
            public AzureSQLDBContext CreateDbContext() => new(options);

            public Task<AzureSQLDBContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new AzureSQLDBContext(options));
        }
    }
}
