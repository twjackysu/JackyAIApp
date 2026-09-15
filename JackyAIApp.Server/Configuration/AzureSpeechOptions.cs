namespace JackyAIApp.Server.Configuration
{
    /// <summary>
    /// Azure AI Speech (text-to-speech) credentials. Optional: when <see cref="Key"/> is empty the
    /// app falls back to OpenAI TTS, the same "configure it and it turns on" shape used by
    /// ApplicationInsights and BlobStorage in Program.cs.
    ///
    /// The key is a secret and comes from Key Vault in production (secret name
    /// <c>AzureSpeech--Key</c>); <see cref="Region"/> is not a secret and lives in appsettings.json.
    /// </summary>
    public class AzureSpeechOptions
    {
        public const string SectionName = "AzureSpeech";

        public string? Key { get; set; }

        /// <summary>Azure region short name, e.g. <c>japaneast</c>. Determines the endpoint host;
        /// a key is only valid for the region its resource was created in.</summary>
        public string? Region { get; set; }
    }
}
