using JackyAIApp.Server.Data.Models.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// What selling a TW stock or ETF would cost: the brokerage fee (0.1425% × the broker's discount) plus
    /// 證交稅 — nothing for a bond or money-market ETF, 0.1% for another ETF, 0.3% for a stock — each floored
    /// to whole dollars, as brokers round. US stocks and funds have other fee schedules and are not modelled.
    /// The buy-side fee is not deducted: the cost basis the user enters is what they paid, fee included.
    /// </summary>
    internal static class TwSellCost
    {
        private const decimal FeeRate = 0.001425m;
        private const decimal EtfTaxRate = 0.001m;
        private const decimal StockTaxRate = 0.003m;

        public static bool AppliesTo(HoldingPosition position) =>
            position.AssetType == AssetType.STOCK && position.Market == "TW";

        /// <param name="valueTWD">The position's market value.</param>
        /// <param name="symbol">Its code: TWSE numbers every ETF "00…".</param>
        /// <param name="exposureFactor">Its <see cref="PortfolioExposure.Factor"/>: 0 is a bond or money-market
        /// ETF (a leveraged one's factor is its multiple, never 0).</param>
        /// <param name="feeDiscountRate">The broker's discount on the fee, 0.28 for 2.8折.</param>
        public static decimal Estimate(decimal valueTWD, string symbol, decimal exposureFactor, decimal feeDiscountRate)
        {
            var taxRate = exposureFactor == 0m ? 0m : symbol.StartsWith("00") ? EtfTaxRate : StockTaxRate;
            var feeRate = FeeRate * feeDiscountRate;
            return Math.Floor(valueTWD * feeRate) + Math.Floor(valueTWD * taxRate);
        }
    }
}
