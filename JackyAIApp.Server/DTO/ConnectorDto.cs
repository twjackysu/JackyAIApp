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