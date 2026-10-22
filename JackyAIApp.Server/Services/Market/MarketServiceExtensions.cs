using System.Net;
using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services.ExternalApi.Twse;
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

        /// <summary>
        /// Registers <see cref="ITwseOpenApi"/>, the one reader of TWSE's OpenAPI, as one process-wide
        /// instance (its datasets are cached for everyone, and the background list refresh shares it).
        /// Its client asks for gzip — the market-wide dumps are megabytes of JSON — and waits up to 90s:
        /// a slow Taiwan-to-US path costs a background fetch nothing, and a request that cannot wait that
        /// long stops waiting on its own token while the fetch finishes for the next caller.
        /// </summary>
        public static IServiceCollection AddTwseOpenApi(this IServiceCollection services)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<ITwseOpenApi, TwseOpenApi>();
            services.AddHttpClient(TwseOpenApi.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(90))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.All,
                })
                .AddReadOnlyResilience();
            return services;
        }
    }
}
