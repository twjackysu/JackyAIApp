using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Scoring
{
    /// <summary>
    /// Configuration for category weights in composite scoring.
    /// Weights are normalized so their sum equals 1.0.
    /// </summary>
    public class CategoryWeightConfig
    {
        /// <summary>
        /// Weight assignments per indicator category.
        /// Default: Technical 50%, Chip 30%, Fundamental 20%.
        /// </summary>
        public Dictionary<IndicatorCategory, decimal> Weights { get; set; } = new()
        {
            [IndicatorCategory.Technical] = 0.50m,
            [IndicatorCategory.Chip] = 0.30m,
            [IndicatorCategory.Fundamental] = 0.20m
        };

        /// <summary>
        /// Get the normalized weight for a category.
        /// If no indicators exist for some categories, remaining weights are redistributed.
        /// </summary>
        public Dictionary<IndicatorCategory, decimal> GetNormalizedWeights(
            IEnumerable<IndicatorCategory> availableCategories)
        {
            var available = availableCategories.ToHashSet();
            var filteredWeights = Weights
                .Where(kv => available.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var totalWeight = filteredWeights.Values.Sum();
            if (totalWeight == 0) return filteredWeights;

            return filteredWeights.ToDictionary(
                kv => kv.Key,
                kv => kv.Value / totalWeight);
        }
    }
}
