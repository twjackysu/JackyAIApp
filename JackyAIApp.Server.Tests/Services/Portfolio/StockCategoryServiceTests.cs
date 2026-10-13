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
            var (category, subCategory, _) = await _service.GetCategoryAsync(AssetType.CASH, "", "現金", "");

            Assert.Equal(PortfolioCategory.Cash, category);
            Assert.Null(subCategory);
            _promptService.Verify(x => x.GetCompletionAsync<CategoryClassificationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()), Times.Never);
            Assert.Empty(await _dbContext.StockSymbolInfos.ToListAsync());
        }

        [Fact]
        public async Task GetCategoryAsync_Liability_ReturnsLiabilityWithoutAiOrDbWrite()
        {
            var (category, subCategory, _) = await _service.GetCategoryAsync(AssetType.LIABILITY, "", "信貸", "");

            Assert.Equal(PortfolioCategory.Liability, category);
            Assert.Null(subCategory);
            _promptService.Verify(x => x.GetCompletionAsync<CategoryClassificationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetCategoryAsync_UncachedSymbol_CallsAiAndPersists()
        {
            _promptService.Setup(x => x.GetCompletionAsync<CategoryClassificationResult>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((new CategoryClassificationResult
                {
                    Category = nameof(PortfolioCategory.Semiconductor),
                    SubCategory = "晶圓代工",
                }, (string?)null));

            var (category, subCategory, _) = await _service.GetCategoryAsync(AssetType.STOCK, "2330", "台積電", "TW");

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

            var (category, subCategory, _) = await _service.GetCategoryAsync(AssetType.STOCK, "2330", "台積電", "TW");

            Assert.Equal(PortfolioCategory.Semiconductor, category);
            Assert.Equal("晶圓代工", subCategory);
            _promptService.Verify(x => x.GetCompletionAsync<CategoryClassificationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetCategoryAsync_AiReturnsUnrecognizedCategory_FallsBackToOther()
        {
            _promptService.Setup(x => x.GetCompletionAsync<CategoryClassificationResult>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((new CategoryClassificationResult
                {
                    Category = "TotallyMadeUpCategory",
                    SubCategory = "???",
                }, (string?)null));

            var (category, _, _) = await _service.GetCategoryAsync(AssetType.STOCK, "9999", "Mystery Corp", "US");

            Assert.Equal(PortfolioCategory.Other, category);
        }

        [Fact]
        public async Task GetCategoryAsync_AiCallFails_FallsBackToOtherWithoutThrowing()
        {
            _promptService.Setup(x => x.GetCompletionAsync<CategoryClassificationResult>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((CategoryClassificationResult?)null, "OpenAI error"));

            var (category, subCategory, _) = await _service.GetCategoryAsync(AssetType.STOCK, "AAPL", "Apple", "US");

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
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
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

        [Fact]
        public async Task GetCategoryAsync_ExistingLabelsInDb_PassesThemAsGroundingContext()
        {
            // The whole point of this grounding: each classification call is otherwise fully
            // independent, so without seeing what's already in use the model has no way to reuse a
            // label instead of inventing a fresh (inconsistent) variant every time.
            _dbContext.StockSymbolInfos.Add(new StockSymbolInfo
            {
                Symbol = "NVDA", Market = "US",
                Category = nameof(PortfolioCategory.Semiconductor), SubCategory = "晶片設計",
                ClassifiedAt = DateTime.UtcNow,
            });
            _dbContext.StockSymbolInfos.Add(new StockSymbolInfo
            {
                Symbol = "0056", Market = "TW",
                Category = nameof(PortfolioCategory.DividendEquity), SubCategory = "高股息台股ETF",
                ClassifiedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();

            string? capturedGrounding = null;
            _promptService.Setup(x => x.GetCompletionAsync<CategoryClassificationResult>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .Callback<string, string, string?, IEnumerable<(string, string)>?, string?>(
                    (_, userMessage, _, _, _) => capturedGrounding = userMessage)
                .ReturnsAsync((new CategoryClassificationResult
                {
                    Category = nameof(PortfolioCategory.Semiconductor),
                    SubCategory = "晶片設計", // reused, not a fresh variant
                }, (string?)null));

            await _service.GetCategoryAsync(AssetType.STOCK, "AMD", "AMD", "US");

            Assert.NotNull(capturedGrounding);
            Assert.Contains("晶片設計", capturedGrounding);
            Assert.Contains("高股息台股ETF", capturedGrounding);
        }

        [Fact]
        public async Task GetCategoryAsync_NoExistingLabels_OmitsGroundingSection()
        {
            string? capturedGrounding = null;
            _promptService.Setup(x => x.GetCompletionAsync<CategoryClassificationResult>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .Callback<string, string, string?, IEnumerable<(string, string)>?, string?>(
                    (_, userMessage, _, _, _) => capturedGrounding = userMessage)
                .ReturnsAsync((new CategoryClassificationResult
                {
                    Category = nameof(PortfolioCategory.Semiconductor),
                    SubCategory = "晶圓代工",
                }, (string?)null));

            await _service.GetCategoryAsync(AssetType.STOCK, "2330", "台積電", "TW");

            Assert.NotNull(capturedGrounding);
            Assert.DoesNotContain("Existing subCategory labels", capturedGrounding);
        }

        [Fact]
        public async Task GetCategoryAsync_AiReturnsTags_SanitizesCapsAndPersistsAsCsv()
        {
            _promptService.Setup(x => x.GetCompletionAsync<CategoryClassificationResult>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((new CategoryClassificationResult
                {
                    Category = nameof(PortfolioCategory.TechSoftware),
                    SubCategory = "軟體服務",
                    // 6 raw entries: a comma-embedded one (must be stripped, or it corrupts the CSV
                    // round-trip), a duplicate, and a blank — only 4 clean distinct ones may survive.
                    Tags = ["雲端", "A,I", "雲端", "  ", "電商", "廣告", "串流"],
                }, (string?)null));

            var (_, _, tags) = await _service.GetCategoryAsync(AssetType.STOCK, "AMZN", "Amazon", "US");

            Assert.Equal(["雲端", "AI", "電商", "廣告"], tags);
            var persisted = await _dbContext.StockSymbolInfos.SingleAsync(s => s.Symbol == "AMZN");
            Assert.Equal("雲端,AI,電商,廣告", persisted.Tags);
        }

        [Fact]
        public async Task GetCategoryAsync_AlreadyClassifiedSymbol_ReturnsPersistedTags()
        {
            _dbContext.StockSymbolInfos.Add(new StockSymbolInfo
            {
                Symbol = "MSFT", Market = "US",
                Category = nameof(PortfolioCategory.TechSoftware), SubCategory = "軟體服務",
                Tags = "軟體,雲端,AI",
                ClassifiedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();

            var (_, _, tags) = await _service.GetCategoryAsync(AssetType.STOCK, "MSFT", "Microsoft", "US");

            Assert.Equal(["軟體", "雲端", "AI"], tags);
            _promptService.Verify(x => x.GetCompletionAsync<CategoryClassificationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetCategoryAsync_ExistingTagsInDb_PassesThemAsGroundingContext()
        {
            // Same reuse-grounding requirement as subCategory: without the existing tag vocabulary
            // in-context, every classification invents near-duplicate variants (雲端/雲端服務)
            // that fragment any tag-based view.
            _dbContext.StockSymbolInfos.Add(new StockSymbolInfo
            {
                Symbol = "GOOG", Market = "US",
                Category = nameof(PortfolioCategory.TechSoftware), SubCategory = "網路服務",
                Tags = "廣告,雲端,AI",
                ClassifiedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();

            string? capturedGrounding = null;
            _promptService.Setup(x => x.GetCompletionAsync<CategoryClassificationResult>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .Callback<string, string, string?, IEnumerable<(string, string)>?, string?>(
                    (_, userMessage, _, _, _) => capturedGrounding = userMessage)
                .ReturnsAsync((new CategoryClassificationResult
                {
                    Category = nameof(PortfolioCategory.TechSoftware),
                    SubCategory = "網路服務",
                    Tags = ["電商", "雲端"],
                }, (string?)null));

            await _service.GetCategoryAsync(AssetType.STOCK, "AMZN", "Amazon", "US");

            Assert.NotNull(capturedGrounding);
            Assert.Contains("Existing tags already in use", capturedGrounding);
            Assert.Contains("廣告", capturedGrounding);
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
