namespace JackyAIApp.Server.DTO
{
    // The connectors the AI may use (docs/ai-native/design/03 §7; the client's
    // apis/connectorsApis/types.ts). Enum-like fields are the client's lower-camel names.

    /// <summary>One tool of a connector, with what the AI may do with it.</summary>
    /// <param name="Effect">read, write or destructive.</param>
    /// <param name="Permission">auto, confirm or disabled.</param>
    public sealed record ConnectorToolDto(string Name, string? Title, string Description, string Effect, string Permission);

    /// <summary>A connector the reader has (or, for the site's own tools, always has).</summary>
    /// <param name="Id">Null for an always-on connector whose settings were never changed (it has no row).</param>
    /// <param name="Category">site, data, dev, productivity or finance.</param>
    /// <param name="AuthKind">none, apiKey, bearer, oauth or existingProvider.</param>
    /// <param name="Health">ok, needsAuth, error, unknown or disabled.</param>
    /// <param name="LastError">Plain words for the owner, never a token.</param>
    public sealed record InstalledConnectorDto(
        string? Id, string DefinitionKey, string DisplayName, string Description, string Category, string? ServerUrl,
        string AuthKind, string Health, bool AlwaysOn, string? LastError, DateTime? LastTestedAt,
        IReadOnlyList<string> Scopes, IReadOnlyList<ConnectorToolDto> Tools);

    /// <summary>One kind of connector the reader may add.</summary>
    public sealed record CatalogEntryDto(
        string Key, string DisplayName, string Description, string Category, string Publisher, string AuthKind,
        string? DocsUrl, bool Installed);

    public sealed record ConnectorCatalogDto(
        IReadOnlyList<CatalogEntryDto> Entries, IReadOnlyList<InstalledConnectorDto> Installed, int CustomLimit);

    /// <summary>The permission the reader picked for one tool.</summary>
    public sealed class ToolPermissionRequestDto
    {
        public string DefinitionKey { get; set; } = string.Empty;

        /// <summary>Null for an always-on connector with no row yet: one is made.</summary>
        public string? InstallationId { get; set; }

        public string ToolName { get; set; } = string.Empty;

        /// <summary>auto, confirm or disabled.</summary>
        public string Permission { get; set; } = string.Empty;
    }

    public sealed class SetEnabledRequestDto
    {
        public bool Enabled { get; set; }
    }

    /// <summary>What the reader typed to reach a server.</summary>
    public sealed class McpCredentialsDto
    {
        /// <summary>For an API key: the header it goes in.</summary>
        public string? HeaderName { get; set; }
        public string? Value { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
    }

    public sealed class CreateInstallationRequestDto
    {
        /// <summary>A catalog entry's key, or <c>custom</c> for an MCP server of the reader's own.</summary>
        public string DefinitionKey { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? ServerUrl { get; set; }

        /// <summary>none, apiKey, bearer or oauth.</summary>
        public string AuthKind { get; set; } = "none";
        public McpCredentialsDto? Credentials { get; set; }
    }

    public sealed class ProbeRequestDto
    {
        public string? ServerUrl { get; set; }
    }

    /// <param name="Auth">none, oauth or apiKey.</param>
    /// <param name="Registration">cimd, dcr or manual, for OAuth; null otherwise.</param>
    public sealed record McpProbeResultDto(
        string ServerUrl, string Auth, string? AuthorizationServer, string? Registration, string? ServerName);

    public sealed record ConnectionTestResultDto(bool Ok, long LatencyMs, string? Error, InstalledConnectorDto Connector);
}
