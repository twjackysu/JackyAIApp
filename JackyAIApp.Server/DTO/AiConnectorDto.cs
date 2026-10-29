namespace JackyAIApp.Server.DTO
{
    // The connectors the AI may use (docs/ai-native/design/03 §7; the client's
    // apis/connectorsApis/types.ts). Enum-like fields are the client's lower-camel names.

    /// <summary>One tool of a connector, with what the AI may do with it.</summary>
    /// <param name="Effect">read, write or destructive.</param>
    /// <param name="Permission">auto, confirm or disabled.</param>
    /// <param name="AlwaysAsks">Never runs on its own, though it can be undone (Studio AI's <c>remember</c>): "auto" is not offered.</param>
    public sealed record ConnectorToolDto(string Name, string? Title, string Description, string Effect, string Permission,
        bool AlwaysAsks = false);

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

    /// <summary>
    /// A remote MCP server listed in the official MCP Registry that the site can connect to (D-122). All of it
    /// is what someone published there: plain text the client shows as text, links https only.
    /// </summary>
    /// <param name="Name">The registry's name (reverse-DNS, e.g. io.github.owner/server).</param>
    /// <param name="Url">Its Streamable HTTP address, in the form an installation keeps.</param>
    /// <param name="RequiredHeaders">Headers it says a client must send (an API key's, or Authorization for a token).</param>
    /// <param name="CatalogKey">The site's own catalog entry at the same address, if there is one.</param>
    public sealed record McpRegistryServerDto(
        string Name, string Title, string Description, string Url, string? WebsiteUrl, string? RepositoryUrl,
        string? Version, IReadOnlyList<string> RequiredHeaders, string? CatalogKey);

    /// <param name="More">The registry has more matches than shown: a longer search narrows them.</param>
    public sealed record McpRegistrySearchDto(IReadOnlyList<McpRegistryServerDto> Servers, bool More);

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

    /// <summary>Where to send the reader to sign in (an authorization server's URL).</summary>
    public sealed record AuthorizeRedirectDto(string RedirectUrl);

    public sealed record ConnectionTestResultDto(bool Ok, long LatencyMs, string? Error, InstalledConnectorDto Connector);

    /// <summary>
    /// A connector's MCP App as the chat draws it (docs/ai-native/design/09 §3): its HTML, the https origins its CSP may
    /// add, whether it wants a border, and whose it is, for the line over the frame the app cannot draw on.
    /// </summary>
    /// <param name="Host">The MCP server's host, in ASCII (a lookalike name shows as punycode).</param>
    public sealed record McpAppDto(string Html, IReadOnlyList<string> ConnectDomains, IReadOnlyList<string> ResourceDomains,
        bool? PrefersBorder, string Connector, string Host);

    /// <summary>A call an MCP App asks of its server (<c>tools/call</c> from the app, design/09 §3).</summary>
    public sealed record McpAppCallRequestDto(string? Tool, System.Text.Json.JsonElement Arguments);

    /// <summary>What the call came to, as the app reads it: a <c>CallToolResult</c> (its text blocks and structured content).</summary>
    public sealed record McpAppCallResultDto(System.Text.Json.JsonElement Result);
}
