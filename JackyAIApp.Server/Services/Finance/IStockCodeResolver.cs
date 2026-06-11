namespace JackyAIApp.Server.Services.Finance
{
    public interface IStockCodeResolver
    {
        /// <summary>
        /// Resolves user input (stock code or company name) to a valid 4-digit TWSE stock code.
        /// Returns an empty string if the input cannot be resolved.
        /// </summary>
        Task<string> ResolveStockCodeAsync(string userInput, CancellationToken cancellationToken = default);
    }
}
