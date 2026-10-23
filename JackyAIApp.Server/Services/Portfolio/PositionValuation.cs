using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// The prices and FX rates one valuation reads, fetched once for today's holdings and the previous
    /// trading day's.
    /// </summary>
    internal sealed record PortfolioPrices(
        Dictionary<string, MarketData> Stocks,
        Dictionary<string, FundNavHistory> Funds,
        PositionFxResolver Fx)
    {
        public LatestPrice? Latest(HoldingPosition position) =>
            AssetValuationPolicy.For(position.AssetType).Latest(position, Stocks, Funds);
    }

    /// <summary>One position valued: its row in the summary, and the bucket its value adds to.</summary>
    internal sealed record ValuedPosition(PositionDto Row, AssetBucket Bucket)
    {
        /// <summary>
        /// Whether its cost, P&amp;L and sell cost count toward the totals: it has a cost basis and a usable
        /// price. Without a price its value is 0, and its P&amp;L would be a fake loss of the whole cost.
        /// </summary>
        public bool CountsTowardPnl => Row.UnrealizedPnlTWD.HasValue;
    }

    internal static class PositionValuation
    {
        public static ValuedPosition Value(HoldingPosition position, PortfolioPrices prices, decimal feeDiscountRate)
        {
            var policy = AssetValuationPolicy.For(position.AssetType);

            // An amount (cash, a debt, a house's value) is "priced" at 1. A market price brings its own
            // currency: a fund's live NAV currency outranks the one stored with the holding.
            decimal currentPrice = 1;
            var priceCurrency = position.Currency;
            if (policy.IsMarketPriced)
            {
                var latest = prices.Latest(position);
                currentPrice = latest?.Price ?? 0;
                if (latest is { } quote) priceCurrency = quote.Currency;
            }
            var priceFailed = policy.IsMarketPriced && currentPrice <= 0;

            var (fxRate, fxSource) = prices.Fx.Resolve(position, priceCurrency);
            // policy.Sign is the one place a value turns negative (a debt): every sum downstream just adds.
            var valueLocal = policy.Sign * (policy.IsMarketPriced ? currentPrice * position.Quantity : position.Quantity);
            var valueTWD = valueLocal * fxRate;
            var exposureFactor = PortfolioExposure.Factor(position.AssetType, position.Name, position.Symbol, position.Market);

            decimal? costTWD = null, pnlTWD = null, pnlPct = null;
            decimal sellCostTWD = 0;
            if (policy.HasCostBasis && position.CostBasis.HasValue)
            {
                // The cost converts by its own currency, which may differ from the price's, through the
                // same resolver as the value.
                costTWD = position.CostBasis.Value * prices.Fx.RateFor(position, position.CostCurrency ?? position.Currency);
                if (!priceFailed)
                {
                    pnlTWD = valueTWD - costTWD;
                    if (TwSellCost.AppliesTo(position))
                    {
                        sellCostTWD = TwSellCost.Estimate(valueTWD, position.Symbol, exposureFactor, feeDiscountRate);
                        pnlTWD -= sellCostTWD;
                    }
                    pnlPct = costTWD > 0 ? Math.Round(pnlTWD.Value / costTWD.Value * 100, 2) : 0;
                }
            }

            var row = new PositionDto
            {
                AssetType = position.AssetType,
                Symbol = position.Symbol,
                Name = position.Name,
                Market = position.Market,
                Currency = priceCurrency,
                CostCurrency = position.CostCurrency,
                Quantity = position.Quantity,
                CurrentPrice = currentPrice,
                MarketValueLocal = valueLocal,
                MarketValueTWD = valueTWD,
                CostBasisTWD = costTWD,
                UnrealizedPnlTWD = pnlTWD,
                UnrealizedPnlPct = pnlPct,
                EstimatedSellCostTWD = sellCostTWD,
                ExposureFactor = exposureFactor,
                FxRateUsed = fxRate == 1m ? null : fxRate,
                FxRateSource = fxSource,
                PriceFetchFailed = priceFailed,
                ValuationMethod = position.ValuationMethod,
            };
            return new ValuedPosition(row, policy.BucketOf(position));
        }
    }
}
