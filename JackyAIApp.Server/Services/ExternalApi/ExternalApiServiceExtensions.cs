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
    }
}
