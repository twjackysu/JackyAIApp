namespace JackyAIApp.Server.DTO
{
    /// <summary>
    /// DTO for connector status response
    /// </summary>
    public class ConnectorStatusDto
    {
        public string Provider { get; set; } = string.Empty;
        public string ProviderDisplayName { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
        public List<string> Services { get; set; } = new();
        public DateTime? ExpiresAt { get; set; }
        public bool RequiresReconnection { get; set; }
    }

    /// <summary>
    /// DTO for OAuth authorization URL response
    /// </summary>
    public class ConnectResponseDto
    {
        public string RedirectUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO for custom OAuth configuration (user-provided credentials)
    /// </summary>
    public class CustomConnectRequestDto
    {
        /// <summary>
        /// Custom OAuth Client ID (optional, uses system default if empty)
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// Custom OAuth Client Secret (optional, uses system default if empty)
        /// </summary>
        public string? ClientSecret { get; set; }

        /// <summary>
        /// Custom Tenant ID for Microsoft (optional, uses system default if empty)
        /// </summary>
        public string? TenantId { get; set; }

        /// <summary>
        /// Custom OAuth Scopes (optional, uses system default if empty)
        /// </summary>
        public string? Scopes { get; set; }
    }

    /// <summary>
    /// OAuth token response from provider
    /// </summary>
    public class OAuthTokenResponse
    {
        public string access_token { get; set; } = string.Empty;
        public string? refresh_token { get; set; }
        public int expires_in { get; set; }
        public string? token_type { get; set; }
        public string? scope { get; set; }
    }
}