namespace JackyAIApp.Server.Configuration
{
    public class Settings
    {
        public required Google Google { get; set; }
    }
    public class Google
    {
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
    }
}
