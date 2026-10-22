using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// What each asset type is, in one table — the facts the Portfolio services used to restate with
    /// their own <c>AssetType is CASH or LIABILITY or REAL_ESTATE</c> checks.
    /// </summary>
    public class AssetValuationPolicyTests
    {
        [Fact]
        public void EveryAssetTypeHasAPolicy()
        {
            foreach (var type in Enum.GetValues<AssetType>())
                Assert.Equal(type, AssetValuationPolicy.For(type).Type);
        }

        [Fact]
        public void AnUndefinedAssetType_HasNone()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AssetValuationPolicy.For((AssetType)99));
        }

        [Theory]
        //          type                    priced sign  cost   liquid  category                  label
        [InlineData(AssetType.STOCK,        true,  1,    true,  true,   null,                     null)]
        [InlineData(AssetType.FUND,         true,  1,    true,  true,   null,                     null)]
        [InlineData(AssetType.CASH,         false, 1,    false, true,   PortfolioCategory.Cash,   "現金")]
        [InlineData(AssetType.LIABILITY,    false, -1,   false, true,   PortfolioCategory.Liability, "負債")]
        [InlineData(AssetType.REAL_ESTATE,  false, 1,    false, false,  PortfolioCategory.Property, "不動產")]
        public void TheFactsOfEachType(
            AssetType type, bool priced, int sign, bool cost, bool liquid, PortfolioCategory? category, string? label)
        {
            var policy = AssetValuationPolicy.For(type);

            Assert.Equal(priced, policy.IsMarketPriced);
            Assert.Equal(sign, policy.Sign);
            Assert.Equal(cost, policy.HasCostBasis);
            Assert.Equal(priced, policy.PaysDividends);
            Assert.Equal(liquid, policy.CountsAsLiquid);
            Assert.Equal(category, policy.FixedCategory);
            Assert.Equal(label, policy.FixedLabel);
        }

        [Theory]
        [InlineData(AssetType.STOCK, "TW", AssetBucket.TwStock)]
        [InlineData(AssetType.STOCK, "US", AssetBucket.UsStock)]
        [InlineData(AssetType.FUND, "", AssetBucket.Fund)]
        [InlineData(AssetType.CASH, "", AssetBucket.Cash)]
        [InlineData(AssetType.LIABILITY, "", AssetBucket.Liability)]
        [InlineData(AssetType.REAL_ESTATE, "", AssetBucket.Property)]
        public void EachHoldingHasOneBucket(AssetType type, string market, AssetBucket expected)
        {
            var position = new HoldingPosition { AssetType = type, Market = market, Currency = "TWD" };

            Assert.Equal(expected, AssetValuationPolicy.For(type).BucketOf(position));
        }

        private static PositionInput Input(AssetType type, string symbol = "", decimal quantity = 1) => new()
        {
            AssetType = type, Symbol = symbol, Currency = "usd", CostCurrency = "", Quantity = quantity,
            Name = "n", Market = "us", UnitPrice = 12.5m, CostBasis = 100m, ValuationMethod = "市價",
        };

        [Fact]
        public void Normalize_AStock_KeepsItsCost_AndNothingItHasNoUseFor()
        {
            var stock = AssetValuationPolicy.For(AssetType.STOCK).Normalize(Input(AssetType.STOCK, "aapl"))!;

            Assert.Equal("AAPL", stock.Symbol);
            Assert.Equal("US", stock.Market);
            Assert.Equal("USD", stock.Currency);
            Assert.Equal("USD", stock.CostCurrency); // defaults to the pricing currency
            Assert.Equal(100m, stock.CostBasis);
            Assert.Null(stock.UnitPrice);
            Assert.Null(stock.ValuationMethod);
        }

        [Fact]
        public void Normalize_KeepsAFundsUnitPrice_AndRealEstatesValuationMethod_Only()
        {
            var fund = AssetValuationPolicy.For(AssetType.FUND).Normalize(Input(AssetType.FUND, "F1"))!;
            var property = AssetValuationPolicy.For(AssetType.REAL_ESTATE).Normalize(Input(AssetType.REAL_ESTATE))!;

            Assert.Equal(12.5m, fund.UnitPrice);
            Assert.Equal("市價", property.ValuationMethod);
            // Real estate's quantity already is its value: no cost, so no P&L or XIRR.
            Assert.Null(property.CostBasis);
            Assert.Null(property.CostCurrency);
            Assert.Null(property.UnitPrice);
        }

        [Theory]
        [InlineData(AssetType.CASH)]
        [InlineData(AssetType.LIABILITY)]
        public void Normalize_AnAmount_NeedsNoSymbol_AndHasNoCost(AssetType type)
        {
            var amount = AssetValuationPolicy.For(type).Normalize(Input(type))!;

            Assert.Null(amount.CostBasis);
            Assert.Null(amount.CostCurrency);
            Assert.Null(amount.UnitPrice);
            Assert.Null(amount.ValuationMethod);
        }

        [Fact]
        public void Normalize_DropsWhatIsNotAHolding()
        {
            Assert.Null(AssetValuationPolicy.For(AssetType.STOCK).Normalize(Input(AssetType.STOCK, " ")));
            Assert.Null(AssetValuationPolicy.For(AssetType.CASH).Normalize(Input(AssetType.CASH, quantity: 0)));
        }

        [Fact]
        public async Task SavingAHolding_OfAnUndefinedType_DropsJustThatRow()
        {
            await using var db = new AzureSQLDBContext(new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var service = new PortfolioServiceBuilder(db).Build();

            await service.SaveHoldingsAsync("u1", new SaveHoldingsRequest
            {
                EffectiveDate = new DateOnly(2026, 10, 1),
                Positions = [Input((AssetType)99, "X"), Input(AssetType.CASH)],
            });

            var saved = await db.HoldingSnapshots.Include(s => s.Positions).SingleAsync();
            Assert.Equal(AssetType.CASH, Assert.Single(saved.Positions).AssetType);
        }

        [Fact]
        public async Task Dividends_ComeFromEachTypesOwnSource()
        {
            var dividends = new Mock<IDividendService>();
            dividends.Setup(d => d.GetStockDividendsAsync("2330", "TW", It.IsAny<CancellationToken>())).ReturnsAsync([]);
            dividends.Setup(d => d.GetFundDividendsAsync("F1", It.IsAny<CancellationToken>())).ReturnsAsync([]);

            await dividends.Object.GetDividendsAsync(AssetType.STOCK, "2330", "TW", default);
            await dividends.Object.GetDividendsAsync(AssetType.FUND, "F1", "", default);

            dividends.Verify(d => d.GetStockDividendsAsync("2330", "TW", It.IsAny<CancellationToken>()), Times.Once);
            dividends.Verify(d => d.GetFundDividendsAsync("F1", It.IsAny<CancellationToken>()), Times.Once);
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = dividends.Object.GetDividendsAsync(AssetType.CASH, "", "", default); });
        }
    }
}
