namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Interface for TWSE data service handling raw data fetching.
    /// </summary>
    public interface ITWSEDataService
    {
        /// <summary>
        /// Fetches raw material information from Taiwan Stock Exchange API with caching.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for timeout control.</param>
        /// <returns>The API response as a string.</returns>
        Task<string> GetRawMaterialInfoWithCacheAsync(CancellationToken cancellationToken = default);
    }
}