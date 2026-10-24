using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>What an MCP server's 401 said about signing in (<c>WWW-Authenticate: Bearer …</c>).</summary>
    /// <param name="ResourceMetadata">Where its protected resource metadata is (RFC 9728), when it says.</param>
    /// <param name="Scope">The scope it asks for, when it says.</param>
    public sealed record McpAuthChallenge(Uri? ResourceMetadata, string? Scope);

    /// <summary>
    /// How a reader signs in to an MCP server, as its metadata says (docs/ai-native/design/03 §5.2): the
    /// server's resource identifier, its authorization server and that server's endpoints.
    /// </summary>
    /// <param name="Resource">The MCP server's resource identifier, sent as <c>resource</c> (RFC 8707).</param>
    /// <param name="Issuer">The authorization server's issuer, checked against the callback's <c>iss</c> (RFC 9207).</param>
    /// <param name="IssParameterSupported">The authorization server always sends <c>iss</c> back.</param>
    /// <param name="ClientIdMetadataDocumentSupported">It takes a URL as client id (Client ID Metadata Documents).</param>
    /// <param name="Scope">What to ask for: the 401's scope, else every scope the server lists; null for none.</param>
    public sealed record McpOAuthServer(
        string Resource,
        string Issuer,
        Uri AuthorizationEndpoint,
        Uri TokenEndpoint,
        Uri? RegistrationEndpoint,
        bool ClientIdMetadataDocumentSupported,
        bool IssParameterSupported,
        string? Scope);

    /// <summary>Why a server's sign-in could not be worked out, in words for the reader.</summary>
    public sealed class McpOAuthException(string message, Exception? inner = null) : Exception(message, inner);

    public interface IMcpOAuthDiscovery
    {
        /// <summary>
        /// Asks the server once, without credentials, what it wants: its challenge when it answers 401, null
        /// when it does not (it needs no sign-in, or another kind). Throws <see cref="McpServerException"/>
        /// when it cannot be reached.
        /// </summary>
        Task<McpAuthChallenge?> ChallengeAsync(Uri server, CancellationToken ct);

        /// <summary>
        /// Follows the server's metadata to its authorization server (MCP authorization: RFC 9728, then
        /// RFC 8414 or OpenID Connect discovery). Throws <see cref="McpOAuthException"/>.
        /// </summary>
        Task<McpOAuthServer> DiscoverAsync(Uri server, McpAuthChallenge challenge, CancellationToken ct);
    }

    /// <summary>
    /// Works out how a reader signs in to an MCP server. Every address it fetches — the server, the
    /// metadata the server points at, the authorization server — came from outside, so every request goes
    /// through <see cref="IMcpHttpHandlerSource"/> (<see cref="SafeHttp"/>: public addresses only, no
    /// redirects), must be https, and is read only up to <see cref="MaxDocumentBytes"/>.
    /// </summary>
    public sealed partial class McpOAuthDiscovery(IMcpHttpHandlerSource handlers) : IMcpOAuthDiscovery
    {
        /// <summary>How long one metadata request may take.</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        /// <summary>The largest metadata document read.</summary>
        public const int MaxDocumentBytes = 64 * 1024;

        private const string ProtectedResourceWellKnown = "/.well-known/oauth-protected-resource";

        public async Task<McpAuthChallenge?> ChallengeAsync(Uri server, CancellationToken ct)
        {
            using var http = Client();
            // An initialize request, the first thing any client sends.
            using var request = new HttpRequestMessage(HttpMethod.Post, server)
            {
                Content = new StringContent(
                    """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"jacky-studio","version":"1.0"}}}""",
                    Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                return response.StatusCode == HttpStatusCode.Unauthorized ? ChallengeOf(response.Headers.WwwAuthenticate) : null;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                throw McpToolClient.Classify(ex, ex is TaskCanceledException);
            }
        }

        public async Task<McpOAuthServer> DiscoverAsync(Uri server, McpAuthChallenge challenge, CancellationToken ct)
        {
            using var http = Client();

            JsonElement? resourceMetadata = null;
            foreach (var url in ProtectedResourceMetadataUrls(server, challenge))
            {
                resourceMetadata = await GetJsonAsync(http, url, ct);
                if (resourceMetadata != null) break;
            }
            if (resourceMetadata is not { } prm)
                throw new McpOAuthException("伺服器需要登入，但沒有提供 OAuth 的設定（protected resource metadata）。");

            // The metadata must be about this server, or another server's sign-in would be used for it.
            var resource = Text(prm, "resource");
            if (resource == null || !Covers(resource, server))
                throw new McpOAuthException("伺服器的 OAuth 設定與網址不符，基於安全不能使用。");

            var issuer = prm.TryGetProperty("authorization_servers", out var servers) && servers.ValueKind == JsonValueKind.Array
                ? servers.EnumerateArray().Select(s => s.ValueKind == JsonValueKind.String ? s.GetString() : null).FirstOrDefault(s => s != null)
                : null;
            if (issuer == null || HttpsUri(issuer) is not { } issuerUri)
                throw new McpOAuthException("伺服器沒有指出可用的授權伺服器。");

            JsonElement? asMetadata = null;
            foreach (var url in AuthorizationServerMetadataUrls(issuerUri))
            {
                asMetadata = await GetJsonAsync(http, url, ct);
                if (asMetadata != null) break;
            }
            if (asMetadata is not { } meta)
                throw new McpOAuthException($"讀不到授權伺服器（{issuerUri.Host}）的設定。");

            // RFC 8414 §3.3: the document is about the issuer it was fetched for.
            if (Text(meta, "issuer") is not { } stated || !SameUrl(stated, issuer))
                throw new McpOAuthException("授權伺服器的設定與它的網址不符，基於安全不能使用。");
            if (HttpsUri(Text(meta, "authorization_endpoint")) is not { } authorize ||
                HttpsUri(Text(meta, "token_endpoint")) is not { } token)
                throw new McpOAuthException("授權伺服器的設定缺少授權或換 token 的網址。");
            // MCP authorization: a client must refuse to go on without PKCE S256.
            if (!Strings(meta, "code_challenge_methods_supported").Contains("S256"))
                throw new McpOAuthException("授權伺服器不支援 PKCE（S256），基於安全不能使用。");

            var scope = challenge.Scope ?? (Strings(prm, "scopes_supported") is { Count: > 0 } listed ? string.Join(' ', listed) : null);
            return new McpOAuthServer(
                resource,
                stated,
                authorize,
                token,
                HttpsUri(Text(meta, "registration_endpoint")),
                Bool(meta, "client_id_metadata_document_supported"),
                Bool(meta, "authorization_response_iss_parameter_supported"),
                string.IsNullOrWhiteSpace(scope) ? null : scope.Trim());
        }

        /// <summary>The challenge in a 401's <c>WWW-Authenticate</c>; empty when it names nothing.</summary>
        internal static McpAuthChallenge ChallengeOf(HttpHeaderValueCollection<AuthenticationHeaderValue> headers)
        {
            var bearer = headers.FirstOrDefault(h => h.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase));
            var parameters = ParametersOf(bearer?.Parameter);
            parameters.TryGetValue("resource_metadata", out var metadata);
            parameters.TryGetValue("scope", out var scope);
            return new McpAuthChallenge(HttpsUri(metadata), string.IsNullOrWhiteSpace(scope) ? null : scope);
        }

        /// <summary>The <c>name="value"</c> (or bare <c>name=value</c>) pairs of an auth-param list.</summary>
        internal static Dictionary<string, string> ParametersOf(string? parameter)
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(parameter)) return found;
            foreach (Match m in AuthParam().Matches(parameter))
            {
                // A quoted-string's backslash only quotes the character after it (RFC 9110 §5.6.4).
                var value = m.Groups["quoted"].Success
                    ? QuotedPair().Replace(m.Groups["quoted"].Value, "$1")
                    : m.Groups["token"].Value;
                found.TryAdd(m.Groups["name"].Value, value);
            }
            return found;
        }

        /// <summary>
        /// Where the server's protected resource metadata may be, in order: the URL its 401 named, then the
        /// well-known URL for its path, then the one at its root (MCP authorization, RFC 9728 §3.1).
        /// </summary>
        internal static IEnumerable<Uri> ProtectedResourceMetadataUrls(Uri server, McpAuthChallenge challenge)
        {
            if (challenge.ResourceMetadata != null) yield return challenge.ResourceMetadata;
            var origin = server.GetLeftPart(UriPartial.Authority);
            var path = server.AbsolutePath.TrimEnd('/');
            if (path.Length > 0) yield return new Uri(origin + ProtectedResourceWellKnown + path);
            yield return new Uri(origin + ProtectedResourceWellKnown);
        }

        /// <summary>
        /// Where an issuer's metadata may be, in the order MCP authorization lists: RFC 8414, then OpenID
        /// Connect discovery (with a path: inserted after the host, then appended to the path).
        /// </summary>
        internal static IEnumerable<Uri> AuthorizationServerMetadataUrls(Uri issuer)
        {
            var origin = issuer.GetLeftPart(UriPartial.Authority);
            var path = issuer.AbsolutePath.TrimEnd('/');
            if (path.Length == 0)
            {
                yield return new Uri(origin + "/.well-known/oauth-authorization-server");
                yield return new Uri(origin + "/.well-known/openid-configuration");
                yield break;
            }
            yield return new Uri(origin + "/.well-known/oauth-authorization-server" + path);
            yield return new Uri(origin + "/.well-known/openid-configuration" + path);
            yield return new Uri(origin + path + "/.well-known/openid-configuration");
        }

        private HttpClient Client() => new(handlers.Create(), disposeHandler: true) { Timeout = RequestTimeout };

        /// <summary>A JSON object at <paramref name="url"/>; null when there is none (any status but 200, not JSON, too large).</summary>
        private static async Task<JsonElement?> GetJsonAsync(HttpClient http, Uri url, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode != HttpStatusCode.OK) return null;
                return await ReadJsonObjectAsync(response, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or IOException or TaskCanceledException)
            {
                // A blocked or unreachable address is a missing document: the next one is tried.
                return null;
            }
        }

        /// <summary>The body as a JSON object, read up to <see cref="MaxDocumentBytes"/>; null otherwise.</summary>
        internal static async Task<JsonElement?> ReadJsonObjectAsync(HttpResponseMessage response, CancellationToken ct)
        {
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[MaxDocumentBytes + 1];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await body.ReadAsync(buffer.AsMemory(read), ct)) > 0) read += n;
            if (read > MaxDocumentBytes) return null;
            try
            {
                using var document = JsonDocument.Parse(buffer.AsMemory(0, read));
                return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        internal static Uri? HttpsUri(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.Fragment)
                ? uri
                : null;

        /// <summary>
        /// Whether a protected resource identifier covers the server's URL: the same origin, and a path the
        /// server's is within (servers often name their origin, e.g. <c>https://mcp.example.com</c> for
        /// <c>…/mcp</c>; the MCP SDKs accept the same). Never another host, scheme or port.
        /// </summary>
        internal static bool Covers(string resource, Uri server)
        {
            if (!Uri.TryCreate(resource, UriKind.Absolute, out var r) || !string.IsNullOrEmpty(r.Query) || !string.IsNullOrEmpty(r.Fragment) ||
                Uri.Compare(r, server, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            var within = r.AbsolutePath.TrimEnd('/');
            var path = server.AbsolutePath.TrimEnd('/');
            return path == within || path.StartsWith(within + "/", StringComparison.Ordinal);
        }

        /// <summary>The same URL, but for the host's case and a trailing slash.</summary>
        internal static bool SameUrl(string a, string b) =>
            Uri.TryCreate(a, UriKind.Absolute, out var x) && Uri.TryCreate(b, UriKind.Absolute, out var y) &&
            Uri.Compare(x, y, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0 &&
            string.Equals(x.PathAndQuery.TrimEnd('/'), y.PathAndQuery.TrimEnd('/'), StringComparison.Ordinal);

        private static string? Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool Bool(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static List<string> Strings(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!).ToList()
                : [];

        [GeneratedRegex("""(?<name>[A-Za-z0-9_\-]+)\s*=\s*(?:"(?<quoted>(?:[^"\\]|\\.)*)"|(?<token>[^\s,"]+))""")]
        private static partial Regex AuthParam();

        [GeneratedRegex(@"\\(.)")]
        private static partial Regex QuotedPair();
    }
}
