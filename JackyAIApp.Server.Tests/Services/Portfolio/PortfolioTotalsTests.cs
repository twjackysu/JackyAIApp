using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>The summary's totals: debt nets out by its sign, real estate stays out of the liquid figures,
    /// and a ratio with nothing to divide by is null (or 0) rather than a division by zero.</summary>
    public class PortfolioTotalsTests
    {
        private static ValuedPosition Valued(AssetBucket bucket, decimal valueTWD, decimal? costTWD = null,
            decimal? pnlTWD = null, decimal sellCost = 0, decimal exposure = 1m) =>
            new(new PositionDto
            {
                MarketValueTWD = valueTWD, CostBasisTWD = costTWD, UnrealizedPnlTWD = pnlTWD,
                EstimatedSellCostTWD = sellCost, ExposureFactor = exposure,
            }, bucket);

        [Fact]
        public void Of_SumsEachBucket_AndDebtNetsOutByItsSign()
        {
            var totals = PortfolioTotals.Of(
            [
                Valued(AssetBucket.TwStock, 100m),
                Valued(AssetBucket.UsStock, 200m),
                Valued(AssetBucket.Cash, 50m, exposure: 0m),
                Valued(AssetBucket.Fund, 30m),
                Valued(AssetBucket.Property, 1_000m, exposure: 0m),
                Valued(AssetBucket.Liability, -400m, exposure: 0m),
            ]);

            Assert.Equal(-400m, totals.Liability);
            Assert.Equal(980m, totals.NetWorth);
            Assert.Equal(380m, totals.LiquidGrossAssets);   // no real estate, debt not netted
            Assert.Equal(-20m, totals.LiquidNetWorth);
        }

        [Fact]
        public void Of_CountsCostAndPnl_OnlyForPositionsWithAPnl()
        {
            var totals = PortfolioTotals.Of(
            [
                Valued(AssetBucket.TwStock, 110m, costTWD: 100m, pnlTWD: 9m, sellCost: 1m),
                // Price failed: its cost is shown on the row but is no part of the P&L.
                Valued(AssetBucket.TwStock, 0m, costTWD: 500m),
            ]);

            Assert.Equal(100m, totals.CostBasis);
            Assert.Equal(9m, totals.UnrealizedPnl);
            Assert.Equal(1m, totals.EstimatedSellCost);
        }

        [Fact]
        public void Exposure_WeighsEachValueByItsFactor()
        {
            var totals = PortfolioTotals.Of([Valued(AssetBucket.TwStock, 100m, exposure: 2m), Valued(AssetBucket.Cash, 100m, exposure: 0m)]);

            Assert.Equal(200m, totals.Exposure);
            Assert.Equal(100.0m, totals.ExposureOfLiquidAssetsPct);
            Assert.Equal(1.00m, totals.LiquidExposureMultiple);
            Assert.Equal(100.0m, totals.DrawdownBufferPct);
            Assert.Equal(5.00m, totals.PctOfLiquidGrossAssets(10m));
        }

        [Fact]
        public void Ratios_WithNothingToDivideBy_AreNullOrZero()
        {
            var totals = PortfolioTotals.Of([Valued(AssetBucket.Liability, -100m, exposure: 0m)]);

            Assert.Null(totals.ExposureOfLiquidAssetsPct);
            Assert.Null(totals.LiquidExposureMultiple);
            Assert.Null(totals.DrawdownBufferPct);
            Assert.Equal(0m, totals.PctOfLiquidGrossAssets(10m));
        }
    }
}
