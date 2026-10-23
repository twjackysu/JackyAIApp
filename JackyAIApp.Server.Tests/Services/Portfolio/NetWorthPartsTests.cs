using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Portfolio;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>The net worth history's own valuation rules for a day, and its series keys.</summary>
    public class NetWorthPartsTests
    {
        private static readonly DateOnly Day = new(2026, 8, 5);

        private static NetWorthDayValuation Valuation(
            Dictionary<string, MarketData>? stocks = null, Dictionary<string, FundNavHistory>? funds = null) =>
            new(stocks ?? [], funds ?? []);

        [Fact]
        public void ADebt_CountsAsItsAmount_Negative() =>
            Assert.Equal(-1_000m, Valuation().ValueOn(
                new HoldingPosition { AssetType = AssetType.LIABILITY, Currency = "TWD", Quantity = 1_000m }, Day, fx: 32m));

        [Fact]
        public void AStockWithNoPriceYet_HasNoValue()
        {
            var stocks = new Dictionary<string, MarketData>
            {
                ["2330"] = new() { StockCode = "2330", HistoricalPrices = [new DailyPrice { Date = Day.AddDays(1).ToDateTime(TimeOnly.MinValue), Close = 1000m }] },
            };

            Assert.Null(Valuation(stocks).ValueOn(
                new HoldingPosition { AssetType = AssetType.STOCK, Symbol = "2330", Currency = "TWD", Quantity = 1 }, Day, fx: 32m));
        }

        [Fact]
        public void AFundWithANavOfZero_FallsBackToItsSavedUnitPrice_InItsNavsCurrency()
        {
            var funds = new Dictionary<string, FundNavHistory>
            {
                ["F1"] = new("USD", [new PricePoint { Date = "2026-08-05", Close = 0m }]),
            };
            var position = new HoldingPosition { AssetType = AssetType.FUND, Symbol = "F1", Currency = "TWD", Quantity = 10, UnitPrice = 12m };

            Assert.Equal(10 * 12m * 32m, Valuation(funds: funds).ValueOn(position, Day, fx: 32m));
        }

        [Theory]
        [InlineData(NetWorthGroupBy.AssetType, "TwStock")]
        [InlineData(NetWorthGroupBy.Category, "Semiconductor")]
        [InlineData(NetWorthGroupBy.SubCategory, "晶圓代工")]
        [InlineData(NetWorthGroupBy.Currency, "TWD")]
        [InlineData(NetWorthGroupBy.Holding, "台積電")]
        public async Task KeyFor_GivesTheSeriesOfEachMode(NetWorthGroupBy groupBy, string key)
        {
            var tsmc = new HoldingPosition { AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電", Market = "TW", Currency = "TWD" };
            var categories = new Mock<IStockCategoryService>();
            categories.Setup(c => c.GetCategoryAsync(AssetType.STOCK, "2330", "台積電", "TW", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StockCategoryInfo(PortfolioCategory.Semiconductor, "晶圓代工", []));

            var grouping = await NetWorthGrouping.LoadAsync(
                groupBy, [new HoldingSnapshot { UserId = "u", EffectiveDate = Day, Positions = [tsmc] }], categories.Object, CancellationToken.None);

            Assert.Equal(key, grouping.KeyFor(tsmc));
        }

        [Fact]
        public async Task Load_ClassifiesOnlyForTheCategoryModes()
        {
            var categories = new Mock<IStockCategoryService>();
            var snapshots = new[] { new HoldingSnapshot { UserId = "u", EffectiveDate = Day,
                Positions = [new HoldingPosition { AssetType = AssetType.STOCK, Symbol = "2330", Currency = "TWD" }] } };

            await NetWorthGrouping.LoadAsync(NetWorthGroupBy.AssetType, snapshots, categories.Object, CancellationToken.None);

            categories.VerifyNoOtherCalls();
        }
    }
}
