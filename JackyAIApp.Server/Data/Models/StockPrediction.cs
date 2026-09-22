namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// One horizon (short/medium/long) of a single `analyze-stock` AI call's bullish/bearish/
    /// neutral prediction, persisted so its accuracy can be checked once the horizon elapses. A
    /// single `analyze-stock` call writes 3 rows (one per horizon) rather than one row with 3
    /// embedded trend columns, so evaluation can query/filter/sort by "which horizon is due"
    /// without parsing embedded fields. See ARCHITECTURE.md → "Finance prediction-accuracy
    /// tracking — Gap Audit item 2" for the full design.
    /// </summary>
    public class StockPrediction
    {
        public required string Id { get; set; }

        public required string StockCode { get; set; }

        /// <summary>"TW" | "US" — matches <c>Services.Finance.DataProviders.MarketRegion</c>'s name.</summary>
        public required string Market { get; set; }

        /// <summary>Who triggered the originating `analyze-stock` call. Traceability only — never
        /// used for access control, this is an internal accuracy metric.</summary>
        public required string UserId { get; set; }

        public required DateTime PredictedAtUtc { get; set; }

        /// <summary>Mirrors <c>StockTrendAnalysis.CurrentPrice</c>, itself nullable. Null means this
        /// prediction has no baseline to compare against and can never be evaluated — the
        /// evaluation query filters these out rather than blocking persistence of the prediction
        /// itself.</summary>
        public decimal? PriceAtPrediction { get; set; }

        public required int HorizonMonthsMin { get; set; }

        public required int HorizonMonthsMax { get; set; }

        /// <summary>"bullish" | "bearish" | "neutral" — matches StockTrendAnalysis's own casing.</summary>
        public required string PredictedTrend { get; set; }

        /// <summary>Null means not yet evaluated — either the horizon hasn't elapsed yet, or it has
        /// but no evaluation pass has picked this row up yet.</summary>
        public DateTime? EvaluatedAtUtc { get; set; }

        /// <summary>"bullish" | "bearish" | "neutral", set once evaluated.</summary>
        public string? ActualTrend { get; set; }

        public decimal? ActualPriceAtEvaluation { get; set; }

        /// <summary><c>ActualTrend == PredictedTrend</c>, set once evaluated.</summary>
        public bool? WasCorrect { get; set; }
    }
}
