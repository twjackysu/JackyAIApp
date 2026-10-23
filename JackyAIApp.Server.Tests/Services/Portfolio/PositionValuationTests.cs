using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>One position valued on its own: price, currency, sign, cost, P&amp;L and the TW sell cost.</summary>
    public class PositionValuationTests
    {
        private static readonly DateOnly Day = new(2026, 10, 20);

        private static PortfolioPrices Prices(
            Dictionary<string, MarketData>? stocks = null, Dictionary<string, FundNavHistory>? funds = null)
        {
            stocks ??= [];
            funds ??= [];
            return new PortfolioPrices(stocks, funds, new PositionFxResolver(32m, new Dictionary<string, decimal>(),
                new Dictionary<(string, DateOnly), decimal>(), stocks, funds));
        }

        private static Dictionary<string, MarketData> Stock(string symbol, decimal close) => new()
        {
            [symbol] = new MarketData
            {
                StockCode = symbol,
                HistoricalPrices = [new DailyPrice { Date = Day.ToDateTime(TimeOnly.MinValue), Close = close }],
            },
        };

        [Fact]
        public void ATwStock_IsPriceTimesQuantity_WithTheSellCostTakenFromItsPnl()
        {
            var position = new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "2330", Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000m,
            };

            var (row, bucket) = PositionValuation.Value(position, Prices(Stock("2330", 1000m)), feeDiscountRate: 0.6m);

            Assert.Equal(AssetBucket.TwStock, bucket);
            Assert.Equal(100_000m, row.MarketValueTWD);
            Assert.Equal(385m, row.EstimatedSellCostTWD);
            Assert.Equal(10_000m - 385m, row.UnrealizedPnlTWD);
            Assert.Null(row.FxRateUsed);
        }

        [Fact]
        public void AStockWithNoPrice_HasNoValueAndNoPnl_ButKeepsItsCost()
        {
            var position = new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "9999", Market = "TW", Currency = "TWD", Quantity = 5, CostBasis = 500m,
            };

            var valued = PositionValuation.Value(position, Prices(), feeDiscountRate: 1m);

            Assert.True(valued.Row.PriceFetchFailed);
            Assert.Equal(0m, valued.Row.MarketValueTWD);
            Assert.Equal(500m, valued.Row.CostBasisTWD);
            Assert.Null(valued.Row.UnrealizedPnlTWD);
            Assert.False(valued.CountsTowardPnl);
        }

        [Fact]
        public void ADebt_IsAnAmountAtPriceOne_AndNegative()
        {
            var position = new HoldingPosition { AssetType = AssetType.LIABILITY, Currency = "TWD", Quantity = 1_000_000m };

            var (row, bucket) = PositionValuation.Value(position, Prices(), feeDiscountRate: 1m);

            Assert.Equal(AssetBucket.Liability, bucket);
            Assert.Equal(1m, row.CurrentPrice);
            Assert.Equal(-1_000_000m, row.MarketValueTWD);
        }

        [Fact]
        public void AFund_TakesItsLiveNavsCurrency_OverTheStoredOne()
        {
            var funds = new Dictionary<string, FundNavHistory>
            {
                ["F1"] = new FundNavHistory("USD", [new PricePoint { Date = "2026-10-20", Close = 10m }]),
            };
            var position = new HoldingPosition { AssetType = AssetType.FUND, Symbol = "F1", Currency = "TWD", Quantity = 10 };

            var row = PositionValuation.Value(position, Prices(funds: funds), feeDiscountRate: 1m).Row;

            Assert.Equal("USD", row.Currency);
            Assert.Equal(10m * 10m * 32m, row.MarketValueTWD);   // no 集保 rate: mid-market
        }
    }
}
