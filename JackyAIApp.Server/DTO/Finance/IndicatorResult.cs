namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Result of a single indicator calculation.
    /// </summary>
    public class IndicatorResult
    {
        /// <summary>Indicator name, e.g., "RSI", "MACD"</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Category this indicator belongs to</summary>
        public IndicatorCategory Category { get; set; }

        /// <summary>Primary computed value</summary>
        public decimal Value { get; set; }

        /// <summary>Additional values (e.g., MACD has Signal and Histogram)</summary>
        public Dictionary<string, decimal> SubValues { get; set; } = new();

        /// <summary>Human-readable signal description</summary>
        public string Signal { get; set; } = string.Empty;

        /// <summary>Signal direction for scoring</summary>
        public SignalDirection Direction { get; set; } = SignalDirection.Neutral;

        /// <summary>Score contribution (0-100) from this indicator</summary>
        public int Score { get; set; }

        /// <summary>Explanation of why this score was given</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Categories for grouping indicators.
    /// </summary>
    public enum IndicatorCategory
    {
        Technical,
        Fundamental,
        Chip
    }

    /// <summary>
    /// Signal direction for scoring logic.
    /// </summary>
    public enum SignalDirection
    {
        StrongBullish,
        Bullish,
        Neutral,
        Bearish,
        StrongBearish
    }
}
