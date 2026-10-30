namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// The canonical names of the OAuth connector providers the API accepts.
    ///
    /// Provider *configuration* (client id, auth/token URLs, scopes, ...) lives in the
    /// <c>Connectors:Providers</c> section of appsettings and is bound to
    /// <see cref="Configuration.ConnectorOptions"/>. The names below are the whitelist the
    /// controllers validate an incoming route value against, and they must match the keys used
    /// in that configuration section — a provider missing here is rejected before
    /// <c>IConnectorService</c> ever gets to look it up.
    /// </summary>
    public static class ConnectorProviders
    {
        public const string Microsoft = "Microsoft";
        public const string Atlassian = "Atlassian";
        public const string Google = "Google";

        /// <summary>
        /// Where a reader connects one: the account connections on the connectors page, below what the AI may use, which the
        /// page scrolls to (the client's <c>ACCOUNTS_ANCHOR</c>, D-225). A native connector's "connect first" card links here.
        /// </summary>
        public const string AccountsPath = "/connectors#accounts";

        /// <summary>Every supported provider, in the order shown to callers.</summary>
        public static readonly IReadOnlyList<string> All = [Microsoft, Atlassian, Google];

        /// <summary>The supported providers as a human-readable list, for error messages.</summary>
        public static readonly string SupportedNames = string.Join(", ", All);

        /// <summary>
        /// Maps a caller-supplied provider name (any casing) to its canonical form,
        /// or null when it is not a supported provider.
        /// </summary>
        public static string? Normalize(string? provider) =>
            provider is null
                ? null
                : All.FirstOrDefault(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase));
    }
}
