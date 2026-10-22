using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>Where a holding's value is shown in the summary and the "by asset type" charts. The
    /// names are the series keys the client draws (net worth history's AssetType mode).</summary>
    public enum AssetBucket { TwStock, UsStock, Fund, Cash, Property, Liability }

    /// <summary>
    /// What one <see cref="AssetType"/> is, as the Portfolio screens need to know it: whether it has a
    /// market price, which way it counts, whether it has a cost, and so on — one class per type
    /// instead of the same <c>AssetType is CASH or LIABILITY or REAL_ESTATE</c> checks in every
    /// service. Facts only: how a screen values a day, or what it does when a price is missing,
    /// stays with that screen (they differ on purpose; see PortfolioAsOfValuationTests).
    /// </summary>
    public abstract class AssetValuationPolicy
    {
        private static readonly Dictionary<AssetType, AssetValuationPolicy> ByType = new AssetValuationPolicy[]
        {
            new StockPolicy(), new FundPolicy(), new CashPolicy(), new LiabilityPolicy(), new RealEstatePolicy(),
        }.ToDictionary(p => p.Type);

        /// <summary>The policy for <paramref name="type"/>. Every defined <see cref="AssetType"/> has one.</summary>
        public static AssetValuationPolicy For(AssetType type) =>
            ByType.TryGetValue(type, out var policy)
                ? policy
                : throw new ArgumentOutOfRangeException(nameof(type), type, "No valuation policy for this asset type.");

        public abstract AssetType Type { get; }

        /// <summary>Valued at a market price × quantity (stocks, funds). The others are a plain amount:
        /// their quantity is their value, with no price to look up.</summary>
        public abstract bool IsMarketPriced { get; }

        /// <summary>-1 for what is owed. Applying it where a value is computed is what lets every sum
        /// further on (net worth, history, charts) just add.</summary>
        public virtual decimal Sign => 1m;

        /// <summary>Has a cost to measure a gain against, so enters P&amp;L, XIRR and the benchmark
        /// comparison. Exactly the market-priced holdings: cash has no gain, debt has no cost, and real
        /// estate's quantity already is its current value.</summary>
        public bool HasCostBasis => IsMarketPriced;

        /// <summary>Pays dividends, fetched per symbol (stocks from Yahoo, funds from FundRich).</summary>
        public bool PaysDividends => IsMarketPriced;

        /// <summary>Counts toward the liquid figures (the summary's day change). Real estate does not.</summary>
        public virtual bool CountsAsLiquid => true;

        /// <summary>The category this type always has; null for the market-priced ones, which are
        /// classified per symbol.</summary>
        public virtual PortfolioCategory? FixedCategory => null;

        /// <summary>The one label all holdings of this type share in the sub-category and per-holding
        /// charts (several cash entries are still one "現金"); null when each holding is labelled itself.</summary>
        public virtual string? FixedLabel => null;

        public abstract AssetBucket BucketOf(HoldingPosition position);

        /// <summary>Keeps the stored unit price (a fund's fallback when its NAV can't be fetched).</summary>
        protected virtual bool StoresUnitPrice => false;

        /// <summary>Keeps how the value was arrived at (real estate: market price, or cost less depreciation).</summary>
        protected virtual bool StoresValuationMethod => false;

        /// <summary>
        /// The position to save for <paramref name="input"/>, or null when it is not one: nothing held,
        /// or a market-priced row without a symbol (even if the client skipped validation). Fields this
        /// type has no use for are dropped, so they can never enter a figure later.
        /// </summary>
        public HoldingPosition? Normalize(PositionInput input)
        {
            if (input.Quantity <= 0 || (IsMarketPriced && string.IsNullOrWhiteSpace(input.Symbol))) return null;

            var currency = string.IsNullOrWhiteSpace(input.Currency) ? "TWD" : input.Currency.ToUpperInvariant();
            return new HoldingPosition
            {
                AssetType = Type,
                Symbol = (input.Symbol ?? "").ToUpperInvariant(),
                Name = input.Name ?? "",
                Market = (input.Market ?? "").ToUpperInvariant(),
                Currency = currency,
                // Cost may be paid in a different currency than the NAV/price (e.g. USD-priced fund
                // bought with TWD). Defaults to the pricing currency when unspecified.
                CostCurrency = HasCostBasis
                    ? (string.IsNullOrWhiteSpace(input.CostCurrency) ? currency : input.CostCurrency.ToUpperInvariant())
                    : null,
                Quantity = input.Quantity,
                UnitPrice = StoresUnitPrice ? input.UnitPrice : null,
                CostBasis = HasCostBasis ? input.CostBasis : null,
                ValuationMethod = StoresValuationMethod ? input.ValuationMethod : null,
            };
        }

        private sealed class StockPolicy : AssetValuationPolicy
        {
            public override AssetType Type => AssetType.STOCK;
            public override bool IsMarketPriced => true;
            public override AssetBucket BucketOf(HoldingPosition position) =>
                position.Market == "US" ? AssetBucket.UsStock : AssetBucket.TwStock;
        }

        private sealed class FundPolicy : AssetValuationPolicy
        {
            public override AssetType Type => AssetType.FUND;
            public override bool IsMarketPriced => true;
            protected override bool StoresUnitPrice => true;
            public override AssetBucket BucketOf(HoldingPosition position) => AssetBucket.Fund;
        }

        private sealed class CashPolicy : AssetValuationPolicy
        {
            public override AssetType Type => AssetType.CASH;
            public override bool IsMarketPriced => false;
            public override PortfolioCategory? FixedCategory => PortfolioCategory.Cash;
            public override string? FixedLabel => "現金";
            public override AssetBucket BucketOf(HoldingPosition position) => AssetBucket.Cash;
        }

        private sealed class LiabilityPolicy : AssetValuationPolicy
        {
            public override AssetType Type => AssetType.LIABILITY;
            public override bool IsMarketPriced => false;
            public override decimal Sign => -1m;
            public override PortfolioCategory? FixedCategory => PortfolioCategory.Liability;
            public override string? FixedLabel => "負債";
            public override AssetBucket BucketOf(HoldingPosition position) => AssetBucket.Liability;
        }

        private sealed class RealEstatePolicy : AssetValuationPolicy
        {
            public override AssetType Type => AssetType.REAL_ESTATE;
            public override bool IsMarketPriced => false;
            public override bool CountsAsLiquid => false;
            protected override bool StoresValuationMethod => true;
            // "Property", not PortfolioCategory.RealEstate: that one is REIT stocks/ETFs, a different
            // concept from this literal owned property.
            public override PortfolioCategory? FixedCategory => PortfolioCategory.Property;
            public override string? FixedLabel => "不動產";
            public override AssetBucket BucketOf(HoldingPosition position) => AssetBucket.Property;
        }
    }
}
