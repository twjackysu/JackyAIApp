namespace JackyAIApp.Server.Configuration
{
    public class Settings
    {
        public required Google Google { get; set; }
        public string? AnthropicApiKey { get; set; }

        /// <summary>Free API key from https://fred.stlouisfed.org for the Indicator Explorer.</summary>
        public string? FredApiKey { get; set; }

        /// <summary>Free API key from https://finnhub.io for market news in the Market Brief.</summary>
        public string? FinnhubApiKey { get; set; }
    }
    public class Google
    {
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
    }
}
