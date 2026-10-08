using JackyAIApp.Server.Extensions;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// Extension methods for registering Portfolio-domain services.
    /// </summary>
    public static class PortfolioServiceExtensions
    {
        public static IServiceCollection AddPortfolioServices(this IServiceCollection services)
        {
            services.AddScoped<IFundDataService, FundDataService>();
            services.AddScoped<IDividendService, DividendService>();
            services.AddScoped<ITdccFxService, TdccFxService>();
            services.AddScoped<ICathayFxService, CathayFxService>();
            services.AddScoped<IStockCategoryService, StockCategoryService>();
            services.AddScoped<IPortfolioService, PortfolioService>();
            services.AddScoped<Common.ServerTiming>();

            // Singleton: holds the search list in memory and opens its own DbContexts through
            // the factory, so the background refresher can share the one instance.
            services.AddSingleton<ITwListedCompanyService, TwListedCompanyService>();
            // The list refresh runs in the background, so it can afford to wait; measured from
            // East US, the two gzipped dumps took 3.5–7.5s each.
            services.AddHttpClient(TwListedCompanyService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(90))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.All,
                })
                .AddReadOnlyResilience();
            services.AddHostedService<TwListedCompanyRefreshService>();

            return services;
        }
    }
}
