namespace JackyAIApp.Server.Configuration
{
    /// <summary>
    /// Configuration options for OAuth connectors
    /// </summary>
    public class ConnectorOptions
    {
        public const string SectionName = "Connectors";

        public Dictionary<string, ProviderConfig> Providers { get; set; } = new();
        public string CallbackBaseUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Configuration for a specific OAuth provider
    /// </summary>
    public class ProviderConfig
    {
        public string DisplayName { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string AuthUrl { get; set; } = string.Empty;
        public string TokenUrl { get; set; } = string.Empty;
        public string Scopes { get; set; } = string.Empty;
        public List<string> Services { get; set; } = new();
    }
}