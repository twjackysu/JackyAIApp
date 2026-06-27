namespace JackyAIApp.Server.Configuration
{
    public class Settings
    {
        public required Google Google { get; set; }
        public string? DifyApiKey { get; set; }

        /// <summary>Free API key from https://fred.stlouisfed.org for the Indicator Explorer.</summary>
        public string? FredApiKey { get; set; }
    }
    public class Google
    {
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
    }
}
