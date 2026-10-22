using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>Where a holding's value is shown in the summary and the "by asset type" charts. The
    /// names are the series keys the client draws (net worth history's AssetType mode).</summary>
    public enum AssetBucket { TwStock, UsStock, Fund, Cash, Property, Liability }

    /// <summary>Where a market-priced holding's price comes from: a stock's quotes (the market data
    /// providers) or a fund's NAV (FundRich). The two are fetched, cached and backfilled separately.</summary>
    public enum PriceSource { None, StockQuote, FundNav }

    /// <summary>The published rate a holding converts at instead of the mid-market one, when there is one.</summary>
    public enum PublishedFxSource
    {
        None,
        /// <summary>集保's rate of the day: what a fund bought through 基富通 actually settles at.</summary>
        Tdcc,
        /// <summary>國泰世華's rate for the day of the holding's own quote (US stocks).</summary>
        CathayOnQuoteDay,
    }

    /// <summary>A holding's latest price among the prices in hand, the currency it is in, and the
    /// day it is from (null when unknown). A price of 0 means there is none to be had.</summary>
    public readonly record struct LatestPrice(decimal Price, string Currency, DateOnly? Day);

    /// <summary>The close on or before a day from a holding's price history, and its currency.
    /// Close is null when the history has nothing that early; what that means is up to the caller.</summary>
    public readonly record struct AsOfPrice(decimal? Close, string Currency);

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

        public virtual PriceSource PriceSource => PriceSource.None;

        public virtual PublishedFxSource PublishedFx => PublishedFxSource.None;

        /// <summary>The price saved with the holding, to value it at when the market can't say: a
        /// fund's unit price. Null for the others.</summary>
        public virtual decimal? StoredPrice(HoldingPosition position) => null;

        /// <summary>The latest price from the fetched maps, falling back to <see cref="StoredPrice"/>;
        /// null when the type has no price (an amount) or, for a stock, when none was fetched.</summary>
        public virtual LatestPrice? Latest(
            HoldingPosition position,
            IReadOnlyDictionary<string, MarketData> stocks, IReadOnlyDictionary<string, FundNavHistory> funds) => null;

        /// <summary>The close on or before <paramref name="date"/> from the holding's own history; null
        /// when there is no history for it at all. Does not fall back to <see cref="StoredPrice"/>: the
        /// screens differ on when that applies.</summary>
        internal virtual AsOfPrice? PriceAsOf(HoldingPosition position, DateOnly date, AsOfPriceBook prices) => null;

        /// <summary>The currency one of the holding's dividends was paid in.</summary>
        public virtual string DividendCurrency(DividendRecord record, string symbol, IReadOnlyDictionary<string, FundNavHistory> funds) =>
            record.Currency;

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
            public override PriceSource PriceSource => PriceSource.StockQuote;
            public override PublishedFxSource PublishedFx => PublishedFxSource.CathayOnQuoteDay;

            public override LatestPrice? Latest(
                HoldingPosition position,
                IReadOnlyDictionary<string, MarketData> stocks, IReadOnlyDictionary<string, FundNavHistory> funds)
            {
                if (!stocks.TryGetValue(position.Symbol, out var data)) return null;
                var last = data.HistoricalPrices.LastOrDefault();
                return new(last?.Close ?? 0, position.Currency, last is null ? null : DateOnly.FromDateTime(last.Date));
            }

            internal override AsOfPrice? PriceAsOf(HoldingPosition position, DateOnly date, AsOfPriceBook prices) =>
                prices.Stocks.TryGetValue(position.Symbol, out var series)
                    ? new AsOfPrice(series.CloseOnOrBefore(date), position.Currency)
                    : null;
        }

        private sealed class FundPolicy : AssetValuationPolicy
        {
            public override AssetType Type => AssetType.FUND;
            public override bool IsMarketPriced => true;
            protected override bool StoresUnitPrice => true;
            public override AssetBucket BucketOf(HoldingPosition position) => AssetBucket.Fund;
            public override PriceSource PriceSource => PriceSource.FundNav;
            public override PublishedFxSource PublishedFx => PublishedFxSource.Tdcc;
            public override decimal? StoredPrice(HoldingPosition position) => position.UnitPrice;

            /// <summary>The latest NAV, in the NAV's currency — FundRich's is authoritative, not the
            /// one picked by hand. Without a usable NAV, the saved unit price; still in the NAV's
            /// currency when there is a NAV history at all.</summary>
            public override LatestPrice? Latest(
                HoldingPosition position,
                IReadOnlyDictionary<string, MarketData> stocks, IReadOnlyDictionary<string, FundNavHistory> funds)
            {
                var stored = position.UnitPrice ?? 0;
                if (string.IsNullOrWhiteSpace(position.Symbol) || !funds.TryGetValue(position.Symbol, out var nav))
                    return new(stored, position.Currency, null);
                var last = nav.Prices.LastOrDefault();
                DateOnly? day = last != null && DateOnly.TryParse(last.Date, out var d) ? d : null;
                return new(last?.Close is > 0 ? last.Close : stored, nav.Currency, day);
            }

            internal override AsOfPrice? PriceAsOf(HoldingPosition position, DateOnly date, AsOfPriceBook prices) =>
                !string.IsNullOrWhiteSpace(position.Symbol) && prices.Funds.TryGetValue(position.Symbol, out var fund)
                    ? new AsOfPrice(fund.Prices.CloseOnOrBefore(date.ToString("yyyy-MM-dd")), fund.Currency)
                    : null;

            /// <summary>FundRich's dividend history has no currency: the fund's NAV currency, else TWD.</summary>
            public override string DividendCurrency(DividendRecord record, string symbol, IReadOnlyDictionary<string, FundNavHistory> funds) =>
                funds.TryGetValue(symbol, out var nav) ? nav.Currency : "TWD";
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
