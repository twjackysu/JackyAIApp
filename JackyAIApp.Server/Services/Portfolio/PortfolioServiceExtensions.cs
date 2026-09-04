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

            return services;
        }
    }
}
