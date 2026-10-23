namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>The summary's totals over its valued positions, and the headline figures built on them.</summary>
    internal sealed class PortfolioTotals
    {
        public decimal TwStock { get; private set; }
        public decimal UsStock { get; private set; }
        public decimal Cash { get; private set; }
        public decimal Fund { get; private set; }
        public decimal RealEstate { get; private set; }

        /// <summary>Negative: what is owed, as the positions carry it (see <see cref="AssetValuationPolicy.Sign"/>).</summary>
        public decimal Liability { get; private set; }

        /// <summary>Over the positions that <see cref="ValuedPosition.CountsTowardPnl"/>.</summary>
        public decimal CostBasis { get; private set; }

        /// <inheritdoc cref="CostBasis"/>
        public decimal UnrealizedPnl { get; private set; }

        /// <inheritdoc cref="CostBasis"/>
        public decimal EstimatedSellCost { get; private set; }

        /// <summary>Σ value × exposure factor: a leveraged ETF counts at its multiple, bonds and cash at 0.</summary>
        public decimal Exposure { get; private set; }

        /// <summary>Everything, net of debt: <see cref="Liability"/> is already negative.</summary>
        public decimal NetWorth => TwStock + UsStock + Cash + Fund + RealEstate + Liability;

        /// <summary>
        /// Liquid assets, not net of debt: the basis of the headline percentages. Real estate is left out,
        /// being valued by hand and not liquid; debt is not netted, so a mortgage does not inflate them.
        /// </summary>
        public decimal LiquidGrossAssets => TwStock + UsStock + Cash + Fund;

        /// <summary>Liquid assets net of debt (流動資產淨值): the money of one's own behind a leveraged position.</summary>
        public decimal LiquidNetWorth => LiquidGrossAssets + Liability;

        /// <summary>How much of what is held carries market risk, however it was financed.</summary>
        public decimal? ExposureOfLiquidAssetsPct =>
            LiquidGrossAssets > 0 ? Math.Round(Exposure / LiquidGrossAssets * 100, 1) : null;

        /// <summary>How big the position is next to one's own money.</summary>
        public decimal? LiquidExposureMultiple =>
            LiquidNetWorth > 0 ? Math.Round(Exposure / LiquidNetWorth, 2) : null;

        /// <summary>
        /// The market-wide fall that would wipe out liquid net worth: solving netWorth + exposure × f = 0
        /// gives netWorth ÷ exposure, the reciprocal of <see cref="LiquidExposureMultiple"/>. Unlike that
        /// ratio it does not blow up and flip sign as one's own money nears zero (the most dangerous point);
        /// it only degenerates at no exposure, the safest. Negative means already insolvent. Assumes every
        /// position moves with the market (beta 1): a conservative estimate, not a precise risk figure.
        /// </summary>
        public decimal? DrawdownBufferPct =>
            Exposure > 0 ? Math.Round(LiquidNetWorth / Exposure * 100, 1) : null;

        /// <summary><paramref name="amount"/> as a % of <see cref="LiquidGrossAssets"/>; 0 without any.</summary>
        public decimal PctOfLiquidGrossAssets(decimal amount) =>
            LiquidGrossAssets > 0 ? Math.Round(amount / LiquidGrossAssets * 100, 2) : 0;

        public static PortfolioTotals Of(IEnumerable<ValuedPosition> positions)
        {
            var totals = new PortfolioTotals();
            foreach (var position in positions)
            {
                var row = position.Row;
                switch (position.Bucket)
                {
                    case AssetBucket.TwStock: totals.TwStock += row.MarketValueTWD; break;
                    case AssetBucket.UsStock: totals.UsStock += row.MarketValueTWD; break;
                    case AssetBucket.Cash: totals.Cash += row.MarketValueTWD; break;
                    case AssetBucket.Fund: totals.Fund += row.MarketValueTWD; break;
                    case AssetBucket.Property: totals.RealEstate += row.MarketValueTWD; break;
                    case AssetBucket.Liability: totals.Liability += row.MarketValueTWD; break;
                }
                if (position.CountsTowardPnl)
                {
                    totals.CostBasis += row.CostBasisTWD!.Value;
                    totals.UnrealizedPnl += row.UnrealizedPnlTWD!.Value;
                    totals.EstimatedSellCost += row.EstimatedSellCostTWD;
                }
                totals.Exposure += row.MarketValueTWD * row.ExposureFactor;
            }
            return totals;
        }
    }
}
