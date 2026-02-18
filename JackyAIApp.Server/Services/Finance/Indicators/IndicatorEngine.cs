using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Aggregates all registered IIndicatorCalculator instances and runs them.
    /// New indicators are automatically picked up via DI registration.
    /// </summary>
    public interface IIndicatorEngine
    {
        /// <summary>
        /// Run all applicable indicators for the given context.
        /// </summary>
        List<IndicatorResult> CalculateAll(IndicatorContext context);

        /// <summary>
        /// Run indicators of a specific category.
        /// </summary>
        List<IndicatorResult> CalculateByCategory(IndicatorContext context, IndicatorCategory category);

        /// <summary>
        /// Run a specific indicator by name.
        /// </summary>
        IndicatorResult? CalculateByName(IndicatorContext context, string name);
    }

    /// <summary>
    /// Default implementation of IIndicatorEngine.
    /// </summary>
    public class IndicatorEngine : IIndicatorEngine
    {
        private readonly IEnumerable<IIndicatorCalculator> _calculators;
        private readonly ILogger<IndicatorEngine> _logger;

        public IndicatorEngine(
            IEnumerable<IIndicatorCalculator> calculators,
            ILogger<IndicatorEngine> logger)
        {
            _calculators = calculators ?? throw new ArgumentNullException(nameof(calculators));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public List<IndicatorResult> CalculateAll(IndicatorContext context)
        {
            var results = new List<IndicatorResult>();

            foreach (var calculator in _calculators)
            {
                try
                {
                    if (calculator.CanCalculate(context))
                    {
                        var result = calculator.Calculate(context);
                        results.Add(result);
                        _logger.LogDebug("Calculated {indicator}: {signal}", calculator.Name, result.Signal);
                    }
                    else
                    {
                        _logger.LogDebug("Skipping {indicator}: insufficient data", calculator.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to calculate indicator {indicator}", calculator.Name);
                }
            }

            return results;
        }

        public List<IndicatorResult> CalculateByCategory(IndicatorContext context, IndicatorCategory category)
        {
            var results = new List<IndicatorResult>();

            foreach (var calculator in _calculators.Where(c => c.Category == category))
            {
                try
                {
                    if (calculator.CanCalculate(context))
                    {
                        results.Add(calculator.Calculate(context));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to calculate indicator {indicator}", calculator.Name);
                }
            }

            return results;
        }

        public IndicatorResult? CalculateByName(IndicatorContext context, string name)
        {
            var calculator = _calculators.FirstOrDefault(c => c.Name == name);
            if (calculator == null)
            {
                _logger.LogWarning("Indicator {name} not found", name);
                return null;
            }

            if (!calculator.CanCalculate(context))
            {
                _logger.LogWarning("Indicator {name} cannot calculate: insufficient data", name);
                return null;
            }

            return calculator.Calculate(context);
        }
    }
}
