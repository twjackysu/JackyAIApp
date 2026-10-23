using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>The cost of selling a TW stock or ETF: the discounted brokerage fee plus 證交稅, each floored.</summary>
    public class TwSellCostTests
    {
        [Theory]
        // 100,000 × 0.1425% × 0.6 = 85.5 → 85; tax 300 (stock), 100 (ETF), 0 (bond ETF).
        [InlineData("2330", 1, 385)]
        [InlineData("0050", 1, 185)]
        [InlineData("00679B", 0, 85)]
        // A leveraged ETF's factor is its multiple: taxed as an ETF.
        [InlineData("00631L", 2, 185)]
        public void Estimate_FeeAtTheDiscountPlusTheTaxOfItsKind(string symbol, decimal exposureFactor, decimal expected) =>
            Assert.Equal(expected, TwSellCost.Estimate(100_000m, symbol, exposureFactor, feeDiscountRate: 0.6m));

        [Fact]
        public void Estimate_FloorsEachPartToWholeDollars() =>
            // fee 12,345 × 0.001425 = 17.59… → 17; tax 37.035 → 37.
            Assert.Equal(54m, TwSellCost.Estimate(12_345m, "2330", 1, feeDiscountRate: 1m));

        [Theory]
        [InlineData(AssetType.STOCK, "TW", true)]
        [InlineData(AssetType.STOCK, "US", false)]
        [InlineData(AssetType.FUND, "TW", false)]
        public void AppliesTo_TwStocksAndEtfsOnly(AssetType type, string market, bool applies) =>
            Assert.Equal(applies, TwSellCost.AppliesTo(new HoldingPosition { AssetType = type, Market = market, Currency = "TWD" }));
    }
}
