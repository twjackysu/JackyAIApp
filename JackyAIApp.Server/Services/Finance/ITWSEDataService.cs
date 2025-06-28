namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Interface for TWSE data service handling raw data fetching and file management.
    /// </summary>
    public interface ITWSEDataService
    {
        /// <summary>
        /// Fetches raw material information from Taiwan Stock Exchange API with caching.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for timeout control.</param>
        /// <returns>The API response as a string.</returns>
        Task<string> GetRawMaterialInfoWithCacheAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets or uploads file to OpenAI with retry mechanism and caching.
        /// </summary>
        /// <param name="content">File content to upload.</param>
        /// <param name="fileName">Name of the file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>File ID from OpenAI.</returns>
        Task<string> GetOrUploadFileWithRetryAsync(string content, string fileName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Ensures vector store is ready with optimized polling.
        /// </summary>
        /// <param name="fileId">File ID to add to vector store.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if vector store is ready.</returns>
        Task<bool> EnsureVectorStoreReadyAsync(string fileId, CancellationToken cancellationToken = default);
    }
}