using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// Service for managing OAuth connectors and token lifecycle
    /// </summary>
    public interface IConnectorService
    {
        /// <summary>
        /// Gets the connection status of all providers for a user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <returns>List of connector statuses</returns>
        Task<ConnectorStatusDto[]> GetUserConnectorStatusAsync(string userId);

        /// <summary>
        /// Initiates OAuth flow for a provider
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="provider">The provider name (Microsoft, Atlassian, Google)</param>
        /// <returns>OAuth authorization URL</returns>
        Task<string> StartConnectAsync(string userId, string provider);

        /// <summary>
        /// Handles OAuth callback and exchanges code for tokens
        /// </summary>
        /// <param name="provider">The provider name</param>
        /// <param name="code">Authorization code from OAuth callback</param>
        /// <param name="state">State parameter from OAuth callback</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> HandleCallbackAsync(string provider, string code, string state);

        /// <summary>
        /// Disconnects a provider for a user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="provider">The provider name</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> DisconnectAsync(string userId, string provider);

        /// <summary>
        /// Refreshes access token if needed
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="provider">The provider name</param>
        /// <returns>True if token was refreshed or still valid, false if refresh failed</returns>
        Task<bool> RefreshTokenIfNeededAsync(string userId, string provider);

        /// <summary>
        /// Gets the decrypted access token for a user and provider
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="provider">The provider name</param>
        /// <returns>The access token if available, null otherwise</returns>
        Task<string?> GetAccessTokenAsync(string userId, string provider);
    }
}