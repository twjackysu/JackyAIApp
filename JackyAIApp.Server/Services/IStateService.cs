namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// Service for managing OAuth state parameters with security validation
    /// </summary>
    public interface IStateService
    {
        /// <summary>
        /// Generates a cryptographically secure state parameter for OAuth flow
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="provider">The OAuth provider name</param>
        /// <returns>Signed state parameter for OAuth authorization URL</returns>
        string GenerateState(string userId, string provider);

        /// <summary>
        /// Validates and extracts information from OAuth state parameter
        /// </summary>
        /// <param name="state">The state parameter received from OAuth callback</param>
        /// <param name="userId">Output: The user ID from the state</param>
        /// <param name="provider">Output: The provider name from the state</param>
        /// <returns>True if state is valid and not expired, false otherwise</returns>
        bool ValidateState(string state, out string userId, out string provider);
    }
}