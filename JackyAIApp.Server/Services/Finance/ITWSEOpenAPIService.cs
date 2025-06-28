namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Interface for Taiwan Stock Exchange Open API service.
    /// </summary>
    public interface ITWSEOpenAPIService
    {
        /// <summary>
        /// Retrieves comprehensive stock data from TWSE Open API endpoints.
        /// </summary>
        /// <param name="stockCode">Stock code to search for.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Formatted stock data as string.</returns>
        Task<string> GetStockDataAsync(string stockCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets stock daily trading data from TWSE API.
        /// </summary>
        /// <param name="stockCode">Stock code.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Daily trading data.</returns>
        Task<string> GetStockDailyTradingAsync(string stockCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets company profile from TWSE API.
        /// </summary>
        /// <param name="stockCode">Stock code.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Company profile data.</returns>
        Task<string> GetCompanyProfileAsync(string stockCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets stock monthly average from TWSE API.
        /// </summary>
        /// <param name="stockCode">Stock code.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Monthly average data.</returns>
        Task<string> GetStockMonthlyAverageAsync(string stockCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets stock valuation ratios from TWSE API.
        /// </summary>
        /// <param name="stockCode">Stock code.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Valuation ratios data.</returns>
        Task<string> GetStockValuationRatiosAsync(string stockCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets company dividend information from TWSE API.
        /// </summary>
        /// <param name="stockCode">Stock code.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Dividend information.</returns>
        Task<string> GetCompanyDividendAsync(string stockCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets market index information from TWSE API.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Market index data.</returns>
        Task<string> GetMarketIndexInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves user input to a valid stock code using AI and company database.
        /// </summary>
        /// <param name="userInput">User input (could be stock code, company name, or partial name).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Valid stock code or empty string if not found.</returns>
        Task<string> ResolveStockCodeAsync(string userInput, CancellationToken cancellationToken = default);
    }
}