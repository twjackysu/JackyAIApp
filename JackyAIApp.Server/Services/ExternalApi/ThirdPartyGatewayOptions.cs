namespace JackyAIApp.Server.Services.ExternalApi
{
    /// <summary>Names of the third parties the gateway paces, one lane each.</summary>
    public static class ThirdPartySources
    {
        /// <summary>證交所 STOCK_DAY — one request per stock per month of history.</summary>
        public const string TwseHistory = "twse-history";

        /// <summary>櫃買中心 daily history — the same shape as <see cref="TwseHistory"/>.</summary>
        public const string TpexHistory = "tpex-history";

        /// <summary>Yahoo Finance's chart API (price history of US stocks and indices).</summary>
        public const string YahooChart = "yahoo-chart";
    }

    /// <summary>
    /// How hard each third party may be hit. Bound from the <c>ThirdPartyGateway</c> configuration
    /// section and read again on every scheduling decision, so a change applies without a restart.
    /// A source that is not listed gets <see cref="Default"/>, which only de-duplicates and bounds
    /// concurrency — it does not pace.
    /// </summary>
    public sealed class ThirdPartyGatewayOptions
    {
        public const string SectionName = "ThirdPartyGateway";

        /// <summary>Settings by source name (see <see cref="ThirdPartySources"/>).</summary>
        public Dictionary<string, ThirdPartySourceOptions> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            // The exchanges publish no rate limit we could find, so these are conservative guesses: a
            // page load's burst goes through at once, a long backfill settles to one request a second.
            [ThirdPartySources.TwseHistory] = new() { MinIntervalMs = 1000 },
            [ThirdPartySources.TpexHistory] = new() { MinIntervalMs = 1000 },
            [ThirdPartySources.YahooChart] = new() { MinIntervalMs = 250, Burst = 40 },
        };

        public ThirdPartySourceOptions Default { get; set; } = new();

        internal ThirdPartySourceOptions For(string source) =>
            Sources.TryGetValue(source, out var options) ? options : Default;
    }

    public sealed class ThirdPartySourceOptions
    {
        /// <summary>
        /// Sustained pace: once the <see cref="Burst"/> is spent, one request per this many
        /// milliseconds. 0 means no pacing.
        /// </summary>
        public int MinIntervalMs { get; set; }

        /// <summary>
        /// Requests that may go out back to back before pacing starts. A normal page load (a refresh
        /// of every held stock) fits inside it and is not slowed; a long backfill is what drains it.
        /// </summary>
        public int Burst { get; set; } = 30;

        /// <summary>
        /// Tokens kept back for <see cref="GatewayPriority.Interactive"/> requests: a
        /// <see cref="GatewayPriority.Background"/> request starts only while more than this many
        /// are available, so a backfill never leaves a user's request waiting behind it.
        /// </summary>
        public int BackgroundReserve { get; set; } = 10;

        /// <summary>Requests of this source running at once.</summary>
        public int MaxConcurrency { get; set; } = 4;

        /// <summary>A request still running after this many seconds is abandoned (0 = no limit).</summary>
        public int RequestTimeoutSeconds { get; set; } = 30;
    }
}
