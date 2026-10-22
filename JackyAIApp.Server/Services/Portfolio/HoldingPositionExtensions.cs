using JackyAIApp.Server.Data.Models.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    public static class HoldingPositionExtensions
    {
        /// <summary>The distinct symbols among <paramref name="positions"/> whose price comes from
        /// <paramref name="source"/>: what to fetch before valuing them.</summary>
        public static List<string> SymbolsPricedBy(this IEnumerable<HoldingPosition> positions, PriceSource source) =>
            positions
                .Where(p => AssetValuationPolicy.For(p.AssetType).PriceSource == source && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();
    }
}
