namespace JackyAIApp.Server.Configuration
{
    public class Settings
    {
        public required Google Google { get; set; }
        public string? DifyApiKey { get; set; }
        public string? DifyApiKey2 { get; set; }
    }
    public class Google
    {
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
    }
}
