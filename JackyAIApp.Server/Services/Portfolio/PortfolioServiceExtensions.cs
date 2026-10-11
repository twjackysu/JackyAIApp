using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;

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

            // 回落煞車 (Issue #431): the TAIEX total-return series, its hourly refresh, and the
            // signal built from it. Singletons, because the client's one-request-at-a-time,
            // 3-second spacing is only a guarantee if there is exactly one of it.
            services.AddHttpClient(TaiexTotalReturnClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
            // Deliberately no AddReadOnlyResilience(): it would retry a failed GET within a
            // second or two, inside the minimum gap and into a rate limit. The refresh retries hourly.
            services.AddSingleton<ITaiexTotalReturnClient, TaiexTotalReturnClient>();
            services.AddSingleton<ITaiexTotalReturnStore, TaiexTotalReturnStore>();
            services.AddSingleton<ITwseHolidayCalendar, TwseHolidayCalendar>();
            services.AddSingleton<ITaiexDataStatus, TaiexDataStatus>();
            services.AddSingleton<IDrawdownBrakeService, DrawdownBrakeService>();
            services.AddSingleton<IDrawdownBrakeAccess, DrawdownBrakeAccess>();
            // One instance behind both faces: the hourly loop and the endpoint's manual refresh must
            // share its gate and cooldown, which only holds if they are the same object.
            services.AddSingleton<TaiexTotalReturnRefreshService>();
            services.AddSingleton<ITaiexTotalReturnRefresher>(sp => sp.GetRequiredService<TaiexTotalReturnRefreshService>());
            services.AddHostedService(sp => sp.GetRequiredService<TaiexTotalReturnRefreshService>());

            return services;
        }
    }
}
