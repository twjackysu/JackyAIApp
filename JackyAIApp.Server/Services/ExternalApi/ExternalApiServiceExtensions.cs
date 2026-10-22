using System.Net;
using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JackyAIApp.Server.Services.ExternalApi
{
    public static class ExternalApiServiceExtensions
    {
        /// <summary>
        /// Registers <see cref="IThirdPartyGateway"/> as one process-wide instance: it holds the queues
        /// and pacing for every caller, so a second instance would be a second, unaware client of the
        /// same third party.
        /// </summary>
        public static IServiceCollection AddThirdPartyGateway(this IServiceCollection services)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddOptions<ThirdPartyGatewayOptions>().BindConfiguration(ThirdPartyGatewayOptions.SectionName);
            services.AddSingleton<IThirdPartyGateway, ThirdPartyGateway>();
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
