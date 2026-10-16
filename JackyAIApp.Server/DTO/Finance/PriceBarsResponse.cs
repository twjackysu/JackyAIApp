namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Daily OHLC bars of a stock or the TAIEX, oldest first — the data behind Stock Intelligence's
    /// trendline chart (the lines themselves are found client-side). Records: one instance is
    /// cached and handed to every request.
    /// </summary>
    /// <param name="Code">Stock code as resolved (e.g. <c>2330</c>, <c>AAPL</c>), or <c>TAIEX</c>.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="AsOf">Date of the last bar.</param>
    /// <param name="Bars">Ascending by date, one per trading day.</param>
    public sealed record PriceBarsResponse(string Code, string Name, DateOnly AsOf, IReadOnlyList<PriceBar> Bars);

    /// <summary>One trading day. For the TAIEX, a day without a usable range from the source has
    /// open = high = low = close.</summary>
    public sealed record PriceBar(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close);
}
