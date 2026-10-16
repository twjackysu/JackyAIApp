using System.Text.Json.Serialization;

namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Daily closes of a market index, oldest first — the data behind Stock Intelligence's
    /// "same period, one line per year" chart. Records, not classes: one instance is cached and
    /// handed to every request, so it must not be changeable after it is built.
    /// </summary>
    /// <param name="Symbol">Source ticker, e.g. <c>^TWII</c>.</param>
    /// <param name="Name">Display name; says whether it is a price or a total-return index.</param>
    /// <param name="Source">Where the numbers come from.</param>
    /// <param name="AsOf">The date of the last point (serialised as <c>yyyy-MM-dd</c>).</param>
    /// <param name="Points">Ascending by date, one per trading day, no gaps filled in.</param>
    public sealed record MarketIndexHistoryResponse(
        string Symbol,
        string Name,
        string Source,
        DateOnly AsOf,
        IReadOnlyList<MarketIndexPoint> Points);

    /// <summary>One trading day's close, rounded to 2 decimals.</summary>
    public sealed record MarketIndexPoint(DateOnly Date, decimal Close)
    {
        // The day's range, for the trendline chart. Kept out of the JSON: the seasonal chart only
        // needs closes, and six years of three more numbers each would triple what it downloads.
        // Null when the source gave no usable range for that day.

        [JsonIgnore] public decimal? Open { get; init; }
        [JsonIgnore] public decimal? High { get; init; }
        [JsonIgnore] public decimal? Low { get; init; }
    }
}
