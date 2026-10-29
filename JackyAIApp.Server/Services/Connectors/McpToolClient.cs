using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>How a request to an MCP server proves who it is for: a header and its value, or nothing.</summary>
    public sealed record McpAuth(string? HeaderName, string? Value)
    {
        public static readonly McpAuth None = new(null, null);

        /// <summary>A bearer token in <c>Authorization</c>.</summary>
        public static McpAuth BearerToken(string token) => new("Authorization", $"Bearer {token}");

        /// <summary>The headers it adds to every request.</summary>
        internal Dictionary<string, string>? Headers() =>
            string.IsNullOrEmpty(HeaderName) || string.IsNullOrEmpty(Value) ? null : new() { [HeaderName] = Value };
    }

    /// <summary>What a server said about itself and the tools it has.</summary>
    public sealed record McpServerTools(string? ServerName, IReadOnlyList<CachedTool> Tools);

    /// <summary>What one call of a server's tool came to, as text for the model.</summary>
    /// <param name="AppResultJson">
    /// The call's result as an MCP App is handed it (<c>ui/notifications/tool-result</c>: its text blocks and
    /// <c>structuredContent</c>, as JSON), or null when it came to more than <see cref="McpToolClient.MaxAppResultChars"/>
    /// (docs/ai-native/design/09 §3).
    /// </param>
    public sealed record McpToolOutcome(string Text, bool IsError, string? AppResultJson = null);

    /// <summary>
    /// A server's MCP App (a <c>ui://</c> resource, <c>text/html;profile=mcp-app</c>) as the chat draws it:
    /// its HTML and what it asked of the frame (<c>_meta.ui.csp</c>, <c>prefersBorder</c>), read leniently.
    /// </summary>
    public sealed record McpAppResource(string Html, IReadOnlyList<string> ConnectDomains, IReadOnlyList<string> ResourceDomains,
        bool? PrefersBorder);

    /// <summary>Why a request to an MCP server did not get an answer.</summary>
    public enum McpFailure
    {
        /// <summary>The server wants credentials, or does not take the ones sent (401/403).</summary>
        NeedsAuth,

        /// <summary>The address is not a public one (<see cref="SafeHttp"/>).</summary>
        Blocked,

        /// <summary>No answer: the server is down, slow, or not an MCP server.</summary>
        Unreachable,
    }

    /// <summary>A request to an MCP server failed; <see cref="Exception.Message"/> is in words for the reader.</summary>
    public sealed class McpServerException(McpFailure failure, string message, Exception? inner = null) : Exception(message, inner)
    {
        public McpFailure Failure { get; } = failure;

        /// <summary>
        /// For a refusal that named the scope the request needs (403 <c>insufficient_scope</c>, MCP authorization's
        /// step-up): that scope, as the server wrote it. Signing in again asks for it too.
        /// </summary>
        public string? RequiredScope { get; init; }
    }

    /// <summary>The HTTP handler requests to a user's MCP server go through.</summary>
    public interface IMcpHttpHandlerSource
    {
        HttpMessageHandler Create();
    }

    /// <summary>Every request through <see cref="SafeHttp"/>: public addresses only, no redirects, no cookies.</summary>
    public sealed class SafeMcpHttpHandlerSource : IMcpHttpHandlerSource
    {
        public HttpMessageHandler Create() => SafeHttp.CreateHandler();
    }

    public interface IMcpToolClient
    {
        /// <summary>The server's tools (every page of them). Throws <see cref="McpServerException"/>.</summary>
        Task<McpServerTools> ListToolsAsync(Uri server, McpAuth auth, CancellationToken ct);

        /// <summary>Calls one tool. A tool's own failure is an outcome with IsError; no answer throws <see cref="McpServerException"/>.</summary>
        Task<McpToolOutcome> CallToolAsync(Uri server, McpAuth auth, string tool, JsonElement arguments, CancellationToken ct);

        /// <summary>
        /// Reads one of the server's MCP Apps (<c>resources/read</c> of a <c>ui://</c> URI): null when what it answers is
        /// not one (another type, no text, too large). No answer throws <see cref="McpServerException"/>.
        /// </summary>
        Task<McpAppResource?> ReadAppAsync(Uri server, McpAuth auth, string uri, CancellationToken ct);
    }

    /// <summary>
    /// A client of a remote MCP server over Streamable HTTP (the official <c>ModelContextProtocol.Core</c>
    /// SDK), through <see cref="IMcpHttpHandlerSource"/> — <see cref="SafeHttp"/> in the app. Each request
    /// is a session of its own (initialize, then the request): nothing is kept between a chat's calls, so
    /// there is no session to lose. An outside server is not trusted: what its tools say about themselves is
    /// read as data (<see cref="EffectOf"/>), and what a call returns goes to the model as untrusted.
    /// </summary>
    public sealed class McpToolClient(IMcpHttpHandlerSource handlers, ILoggerFactory loggerFactory) : IMcpToolClient
    {
        /// <summary>How long one request (with its session's start) may take.</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        /// <summary>The most tools of one server kept; a server listing more is cut.</summary>
        public const int MaxTools = 100;

        /// <summary>
        /// The most one response of a server may hold (<see cref="ResponseSizeLimit"/>): far more than a list of
        /// <see cref="MaxTools"/> tools or a tool's answer for the model, far less than would strain the app.
        /// </summary>
        public const long MaxResponseBytes = 8 * 1024 * 1024;

        /// <summary>The most pages of <c>tools/list</c> read: a server that keeps naming a next page is not followed further.</summary>
        public const int MaxToolPages = 5;

        /// <summary>
        /// The longest input schema of a tool kept (as JSON text); a tool with a longer one is left out. Every schema is
        /// stored with the connector and sent to the model each turn, so one server cannot make either huge.
        /// </summary>
        public const int MaxSchemaChars = 32_000;

        /// <summary>The most schema text of all of one server's tools kept; the tools after it are left out.</summary>
        public const int MaxAllSchemasChars = 400_000;

        /// <summary>The most an app's result (<see cref="McpToolOutcome.AppResultJson"/>) may come to; a larger one is not handed to the app.</summary>
        public const int MaxAppResultChars = 64 * 1024;

        /// <summary>The most an app's HTML may be; a larger app is not drawn.</summary>
        public const int MaxAppHtmlChars = 512 * 1024;

        /// <summary>The longest <c>ui://</c> URI of an app kept.</summary>
        public const int MaxAppUriChars = 512;

        /// <summary>The MIME type of an MCP App's HTML (the <c>io.modelcontextprotocol/ui</c> extension).</summary>
        public const string AppMimeType = "text/html;profile=mcp-app";

        /// <summary>The most hosts of each kind an app's CSP may name; more are left out.</summary>
        private const int MaxCspDomains = 20;

        private const int MaxDescriptionChars = 1_000;
        private const int MaxTitleChars = 100;
        private const int MaxNameChars = 128;

        public async Task<McpServerTools> ListToolsAsync(Uri server, McpAuth auth, CancellationToken ct)
        {
            return await WithClientAsync(server, auth, async (client, token) =>
            {
                // Page by page rather than the SDK's whole list, which follows the server's next page for as long as it names one.
                var kept = new List<CachedTool>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                var schemaChars = 0;
                var request = new ListToolsRequestParams();
                for (var page = 0; page < MaxToolPages && kept.Count < MaxTools; page++)
                {
                    var listed = await client.ListToolsAsync(request, token);
                    foreach (var tool in listed.Tools)
                    {
                        if (kept.Count >= MaxTools || schemaChars >= MaxAllSchemasChars) break;
                        if (string.IsNullOrEmpty(tool.Name) || tool.Name.Length > MaxNameChars || !names.Add(tool.Name)) continue;
                        var cached = ToCached(tool);
                        var length = cached.InputSchema.GetRawText().Length;
                        if (length > MaxSchemaChars || schemaChars + length > MaxAllSchemasChars) continue;
                        schemaChars += length;
                        kept.Add(cached);
                    }
                    if (string.IsNullOrEmpty(listed.NextCursor)) break;
                    request = new ListToolsRequestParams { Cursor = listed.NextCursor };
                }
                return new McpServerTools(client.ServerInfo.Title ?? client.ServerInfo.Name, kept);
            }, ct);
        }

        public async Task<McpToolOutcome> CallToolAsync(Uri server, McpAuth auth, string tool, JsonElement arguments, CancellationToken ct)
        {
            var args = arguments.ValueKind == JsonValueKind.Object
                ? arguments.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
                : new Dictionary<string, JsonElement>();
            return await WithClientAsync(server, auth, async (client, token) =>
            {
                var result = await client.CallToolAsync(new CallToolRequestParams { Name = tool, Arguments = args }, token);
                return new McpToolOutcome(TextOf(result), result.IsError == true, AppResultOf(result));
            }, ct);
        }

        public async Task<McpAppResource?> ReadAppAsync(Uri server, McpAuth auth, string uri, CancellationToken ct)
        {
            if (AppUriOf(uri) is not { } appUri) return null;
            return await WithClientAsync(server, auth, async (client, token) =>
            {
                var read = await client.ReadResourceAsync(new ReadResourceRequestParams { Uri = appUri }, token);
                return AppOf(read, appUri);
            }, ct);
        }

        /// <summary>The app among what <c>resources/read</c> answered: the text of that URI with the app's MIME type, within the size kept.</summary>
        internal static McpAppResource? AppOf(ReadResourceResult read, string uri)
        {
            foreach (var contents in read.Contents)
            {
                if (contents is not TextResourceContents text || !string.Equals(contents.Uri, uri, StringComparison.Ordinal)) continue;
                if (!IsAppMimeType(contents.MimeType)) continue;
                if (string.IsNullOrWhiteSpace(text.Text) || text.Text.Length > MaxAppHtmlChars) return null;
                var ui = contents.Meta?["ui"] as JsonObject;
                var csp = ui?["csp"] as JsonObject;
                return new McpAppResource(
                    text.Text,
                    HttpsOrigins(csp?["connectDomains"]),
                    HttpsOrigins(csp?["resourceDomains"]),
                    ui?["prefersBorder"] is JsonValue border && border.TryGetValue<bool>(out var b) ? b : null);
            }
            return null;
        }

        // The type with its profile, in any case and spacing ("text/html; profile=mcp-app").
        private static bool IsAppMimeType(string? mimeType) =>
            mimeType != null && string.Equals(mimeType.Replace(" ", ""), AppMimeType, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The hosts an app's CSP may name, each as an https origin (<c>https://host[:port]</c>): no other scheme, no
        /// wildcard, path or anything a CSP would read as more than one source. The rest are left out (design/09 §4).
        /// </summary>
        internal static IReadOnlyList<string> HttpsOrigins(JsonNode? domains)
        {
            if (domains is not JsonArray array) return [];
            var kept = new List<string>();
            foreach (var item in array)
            {
                if (kept.Count >= MaxCspDomains) break;
                if (item is not JsonValue value || !value.TryGetValue<string>(out var text)) continue;
                if (text.Length > 200 || text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '*' or ';' or ',' or '\'')) continue;
                if (!Uri.TryCreate(text, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps) continue;
                if (origin.AbsolutePath != "/" || origin.Query.Length > 0 || origin.Fragment.Length > 0 || origin.UserInfo.Length > 0) continue;
                var written = origin.IsDefaultPort ? $"https://{origin.IdnHost}" : $"https://{origin.IdnHost}:{origin.Port}";
                if (!kept.Contains(written, StringComparer.Ordinal)) kept.Add(written);
            }
            return kept;
        }

        /// <summary>A <c>ui://</c> URI as kept: the scheme, within <see cref="MaxAppUriChars"/>, nothing a header or log line would break on.</summary>
        internal static string? AppUriOf(string? uri) =>
            uri != null && uri.StartsWith("ui://", StringComparison.Ordinal) && uri.Length is > 5 and <= MaxAppUriChars &&
            !uri.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
                ? uri
                : null;

        /// <summary>
        /// A call's result as an app is handed it: its text blocks (other blocks are left out) and its structured content,
        /// as JSON, or null when that comes to more than <see cref="MaxAppResultChars"/>.
        /// </summary>
        internal static string? AppResultOf(CallToolResult result)
        {
            var content = new JsonArray();
            foreach (var block in result.Content)
            {
                if (block is TextContentBlock t) content.Add(new JsonObject { ["type"] = "text", ["text"] = t.Text });
            }
            var app = new JsonObject { ["content"] = content };
            if (result.StructuredContent is { ValueKind: JsonValueKind.Object } structured)
                app["structuredContent"] = JsonNode.Parse(structured.GetRawText());
            if (result.IsError == true) app["isError"] = true;
            var json = app.ToJsonString();
            return json.Length > MaxAppResultChars ? null : json;
        }

        private async Task<T> WithClientAsync<T>(
            Uri server, McpAuth auth, Func<McpClient, CancellationToken, Task<T>> request, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            var refusals = new RefusalRecorder(new ResponseSizeLimit(handlers.Create(), MaxResponseBytes));
            var http = new HttpClient(refusals, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            try
            {
                await using var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = server,
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = auth.Headers(),
                    ConnectionTimeout = RequestTimeout,
                }, http, loggerFactory, ownsHttpClient: true);
                await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
                {
                    ClientInfo = new Implementation { Name = "jacky-studio", Version = "1.0" },
                }, loggerFactory, timeout.Token);
                return await request(client, timeout.Token);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (Exception ex) when (ex is not McpServerException)
            {
                var failure = Classify(ex, timeout.IsCancellationRequested);
                if (failure.Failure == McpFailure.NeedsAuth && refusals.InsufficientScope is { } scope)
                    throw new McpServerException(McpFailure.NeedsAuth, "這個動作需要更多權限，請重新授權。", ex) { RequiredScope = scope };
                throw failure;
            }
        }

        /// <summary>
        /// Keeps the scope the server's refusal asked for, which the SDK's exception does not carry: a 403 (or 401)
        /// whose <c>WWW-Authenticate</c> says <c>error="insufficient_scope"</c> with a <c>scope</c>.
        /// </summary>
        private sealed class RefusalRecorder(HttpMessageHandler inner) : DelegatingHandler(inner)
        {
            public string? InsufficientScope { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var response = await base.SendAsync(request, ct);
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                    InsufficientScope = InsufficientScopeOf(response.Headers.WwwAuthenticate) ?? InsufficientScope;
                return response;
            }
        }

        /// <summary>The scope a Bearer challenge says is missing (RFC 6750 §3.1 <c>insufficient_scope</c>); null for any other.</summary>
        internal static string? InsufficientScopeOf(HttpHeaderValueCollection<AuthenticationHeaderValue> headers)
        {
            foreach (var challenge in headers)
            {
                if (!string.Equals(challenge.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)) continue;
                var parameters = McpOAuthDiscovery.ParametersOf(challenge.Parameter);
                if (parameters.TryGetValue("error", out var error) && error == "insufficient_scope" &&
                    parameters.TryGetValue("scope", out var scope) && !string.IsNullOrWhiteSpace(scope))
                    return scope.Trim();
            }
            return null;
        }

        /// <summary>What a failure of a request comes to, in words for the reader (never the server's own text).</summary>
        internal static McpServerException Classify(Exception ex, bool timedOut)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is ResponseTooLargeException)
                    return new McpServerException(McpFailure.Unreachable, "伺服器的回應太大，沒有讀取。", ex);
                if (e is BlockedAddressException)
                    return new McpServerException(McpFailure.Blocked, "這個網址不是公開的網路位址，基於安全不能連線。", ex);
                if (e is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
                    return new McpServerException(McpFailure.NeedsAuth, "伺服器需要登入，或不接受目前的認證資料。", ex);
            }
            return timedOut
                ? new McpServerException(McpFailure.Unreachable, "伺服器沒有在時限內回應。", ex)
                : new McpServerException(McpFailure.Unreachable, "連不上這個 MCP server，請確認網址是 Streamable HTTP 的 MCP 端點。", ex);
        }

        /// <summary>
        /// What calling a tool does, from what its server says (MCP tool annotations, which are hints a
        /// server may get wrong): a read only when it says so; destructive when it says so; else a write.
        /// </summary>
        internal static ToolEffect EffectOf(ToolAnnotations? annotations) =>
            annotations?.ReadOnlyHint == true ? ToolEffect.Read
            : annotations?.DestructiveHint == true ? ToolEffect.Destructive
            : ToolEffect.Write;

        internal static CachedTool ToCached(Tool tool)
        {
            var ui = tool.Meta?["ui"] as JsonObject;
            return new(
                tool.Name,
                CutTitle(tool.Title ?? tool.Annotations?.Title),
                Cut(tool.Description ?? ""),
                EffectOf(tool.Annotations),
                tool.InputSchema.ValueKind == JsonValueKind.Object
                    ? tool.InputSchema.Clone()
                    : JsonSerializer.SerializeToElement(new { type = "object" }),
                ui?["resourceUri"] is JsonValue uri && uri.TryGetValue<string>(out var text) ? AppUriOf(text) : null,
                AppOnlyOf(ui?["visibility"]));
        }

        /// <summary>
        /// Whether a tool is for its app alone (<c>_meta.ui.visibility</c> names <c>app</c> and not <c>model</c>): such a tool
        /// is not offered to the model. A list naming neither, or none, is the default, both.
        /// </summary>
        internal static bool AppOnlyOf(JsonNode? visibility)
        {
            if (visibility is not JsonArray array) return false;
            var named = array.OfType<JsonValue>()
                .Select(v => v.TryGetValue<string>(out var s) ? s : null)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            return named.Contains("app") && !named.Contains("model");
        }

        /// <summary>A call's result as text: its text blocks, a note for each other block, or its structured content.</summary>
        internal static string TextOf(CallToolResult result)
        {
            var text = new StringBuilder();
            foreach (var block in result.Content)
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(block is TextContentBlock t ? t.Text : $"[{block.Type}]");
            }
            if (text.Length == 0 && result.StructuredContent is { } structured)
                text.Append(structured.GetRawText());
            return text.ToString();
        }

        private static string? CutTitle(string? title) => title is { Length: > MaxTitleChars } ? title[..MaxTitleChars] + "…" : title;

        private static string Cut(string s) => s.Length > MaxDescriptionChars ? s[..MaxDescriptionChars] + "…" : s;
    }
}
