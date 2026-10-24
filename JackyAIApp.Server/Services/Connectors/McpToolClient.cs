using System.Net;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>How a request to an MCP server proves who it is for: a header and its value, or nothing.</summary>
    public sealed record McpAuth(string? HeaderName, string? Value)
    {
        public static readonly McpAuth None = new(null, null);

        /// <summary>The headers it adds to every request.</summary>
        internal Dictionary<string, string>? Headers() =>
            string.IsNullOrEmpty(HeaderName) || string.IsNullOrEmpty(Value) ? null : new() { [HeaderName] = Value };
    }

    /// <summary>What a server said about itself and the tools it has.</summary>
    public sealed record McpServerTools(string? ServerName, IReadOnlyList<CachedTool> Tools);

    /// <summary>What one call of a server's tool came to, as text for the model.</summary>
    public sealed record McpToolOutcome(string Text, bool IsError);

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

        private const int MaxDescriptionChars = 1_000;

        public async Task<McpServerTools> ListToolsAsync(Uri server, McpAuth auth, CancellationToken ct)
        {
            return await WithClientAsync(server, auth, async (client, token) =>
            {
                var tools = await client.ListToolsAsync(cancellationToken: token);
                var cached = tools.Take(MaxTools).Select(t => ToCached(t.ProtocolTool)).ToList();
                return new McpServerTools(client.ServerInfo.Title ?? client.ServerInfo.Name, cached);
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
                return new McpToolOutcome(TextOf(result), result.IsError == true);
            }, ct);
        }

        private async Task<T> WithClientAsync<T>(
            Uri server, McpAuth auth, Func<McpClient, CancellationToken, Task<T>> request, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            var http = new HttpClient(handlers.Create(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
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
                throw Classify(ex, timeout.IsCancellationRequested);
            }
        }

        /// <summary>What a failure of a request comes to, in words for the reader (never the server's own text).</summary>
        internal static McpServerException Classify(Exception ex, bool timedOut)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
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

        internal static CachedTool ToCached(Tool tool) => new(
            tool.Name,
            tool.Title ?? tool.Annotations?.Title,
            Cut(tool.Description ?? ""),
            EffectOf(tool.Annotations),
            tool.InputSchema.ValueKind == JsonValueKind.Object
                ? tool.InputSchema.Clone()
                : JsonSerializer.SerializeToElement(new { type = "object" }));

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

        private static string Cut(string s) => s.Length > MaxDescriptionChars ? s[..MaxDescriptionChars] + "…" : s;
    }
}
