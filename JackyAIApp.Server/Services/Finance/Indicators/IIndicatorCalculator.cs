using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Strategy interface for individual indicator calculations.
    /// Each indicator is a self-contained calculator that can be independently
    /// added, removed, or modified without affecting other indicators.
    /// </summary>
    public interface IIndicatorCalculator
    {
        /// <summary>Unique name of this indicator</summary>
        string Name { get; }

        /// <summary>Category for grouping (Technical, Fundamental, Chip)</summary>
        IndicatorCategory Category { get; }

        /// <summary>
        /// Whether this calculator can run with the available data.
        /// </summary>
        bool CanCalculate(IndicatorContext context);

        /// <summary>
        /// Calculate the indicator value and scoring.
        /// </summary>
        IndicatorResult Calculate(IndicatorContext context);
    }
}
