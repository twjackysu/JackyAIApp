using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JackyAIApp.Server.Services.Market
{
    public static class MarketServiceExtensions
    {
        /// <summary>
        /// The TWSE trading calendar and the background load of its holiday schedule. Called by every
        /// module that uses the calendar (Finance, Portfolio); registering it more than once is a no-op.
        /// </summary>
        public static IServiceCollection AddTradingCalendar(this IServiceCollection services)
        {
            services.TryAddSingleton<TwseTradingCalendar>();
            services.TryAddSingleton<ITradingCalendar>(sp => sp.GetRequiredService<TwseTradingCalendar>());
            services.AddHostedService<TradingCalendarRefreshService>();
            return services;
        }
    }
}
