using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JackyAIApp.Server.Configuration;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// A reader's OAuth sign-in to one MCP server, as stored (encrypted, <see cref="ConnectorSecrets"/>): the
    /// authorization server it was worked out from (<see cref="McpOAuthServer"/>), the client we are there,
    /// and the tokens once the reader signed in.
    /// </summary>
    public sealed record McpOAuthGrant(
        string Issuer,
        bool IssParameterSupported,
        string AuthorizationEndpoint,
        string TokenEndpoint,
        string Resource,
        string? Scope,
        string ClientId,
        string? ClientSecret,
        string? AccessToken = null,
        string? RefreshToken = null,
        DateTime? ExpiresAt = null)
    {
        /// <summary>The sign-in, before the reader signed in, for a server and the client we are there.</summary>
        public static McpOAuthGrant For(McpOAuthServer server, string clientId, string? clientSecret) => new(
            server.Issuer, server.IssParameterSupported, server.AuthorizationEndpoint.ToString(), server.TokenEndpoint.ToString(),
            server.Resource, server.Scope, clientId, clientSecret);
    }

    /// <summary>
    /// The site's own addresses in an MCP sign-in: where the authorization server sends the reader back, and
    /// our client metadata document (a client id that is a URL; none when the site is not on a public https
    /// address, e.g. localhost, which no authorization server can fetch).
    /// </summary>
    public sealed record McpOAuthUrls(string RedirectUri, string? ClientMetadataUrl)
    {
        public const string CallbackPath = "/mcp";

        /// <summary>From <c>Connectors:CallbackBaseUrl</c> (…/api/connectors/callback), the account connections' own.</summary>
        public static McpOAuthUrls From(ConnectorOptions options)
        {
            var callback = options.CallbackBaseUrl.TrimEnd('/');
            const string suffix = "/callback";
            var connectors = callback.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? callback[..^suffix.Length] : callback;
            var metadata = Uri.TryCreate(connectors + "/oauth/client-metadata", UriKind.Absolute, out var uri) &&
                           uri.Scheme == Uri.UriSchemeHttps && !uri.IsLoopback && uri.Host.Contains('.')
                ? uri.ToString()
                : null;
            return new McpOAuthUrls(callback + CallbackPath, metadata);
        }

        /// <summary>How we become a client of <paramref name="server"/>: cimd, dcr or manual (the reader registers one).</summary>
        public string RegistrationFor(McpOAuthServer server) =>
            server.ClientIdMetadataDocumentSupported && ClientMetadataUrl != null ? "cimd"
            : server.RegistrationEndpoint != null ? "dcr"
            : "manual";
    }

    /// <summary>What an authorization server's token endpoint gave.</summary>
    public sealed record McpOAuthTokenResponse(string AccessToken, string? RefreshToken, TimeSpan? ExpiresIn);

    /// <summary>Requests to an authorization server: registering as its client, and getting tokens.</summary>
    public interface IMcpOAuthEndpoints
    {
        /// <summary>Dynamic client registration (RFC 7591) as a public client. Throws <see cref="McpOAuthException"/>.</summary>
        Task<(string ClientId, string? ClientSecret)> RegisterAsync(Uri registrationEndpoint, string redirectUri, CancellationToken ct);

        /// <summary>A token request (<paramref name="form"/> without the client's credentials, which this adds). Throws <see cref="McpOAuthException"/>.</summary>
        Task<McpOAuthTokenResponse> RequestTokenAsync(
            Uri tokenEndpoint, IReadOnlyDictionary<string, string> form, string clientId, string? clientSecret, CancellationToken ct);
    }

    /// <summary>
    /// <see cref="IMcpOAuthEndpoints"/> through <see cref="IMcpHttpHandlerSource"/> (<see cref="SafeHttp"/>):
    /// the endpoints came from an outside server's metadata. Only https; nothing the server says goes to the
    /// reader but an OAuth error code.
    /// </summary>
    public sealed partial class McpOAuthEndpoints(IMcpHttpHandlerSource handlers) : IMcpOAuthEndpoints
    {
        public const string ClientName = "Jacky Studio";

        public async Task<(string ClientId, string? ClientSecret)> RegisterAsync(Uri registrationEndpoint, string redirectUri, CancellationToken ct)
        {
            using var http = Client();
            var body = new Dictionary<string, object>
            {
                ["client_name"] = ClientName,
                ["redirect_uris"] = new[] { redirectUri },
                ["grant_types"] = new[] { "authorization_code", "refresh_token" },
                ["response_types"] = new[] { "code" },
                ["token_endpoint_auth_method"] = "none",
            };
            var json = await SendAsync(http, new HttpRequestMessage(HttpMethod.Post, registrationEndpoint) { Content = JsonContent.Create(body) },
                "無法向授權伺服器註冊（dynamic client registration）。", ct);
            var clientId = Text(json, "client_id");
            if (string.IsNullOrEmpty(clientId)) throw new McpOAuthException("授權伺服器的註冊回應沒有 client_id。");
            return (clientId, Text(json, "client_secret"));
        }

        public async Task<McpOAuthTokenResponse> RequestTokenAsync(
            Uri tokenEndpoint, IReadOnlyDictionary<string, string> form, string clientId, string? clientSecret, CancellationToken ct)
        {
            using var http = Client();
            var fields = new Dictionary<string, string>(form);
            var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
            if (string.IsNullOrEmpty(clientSecret))
            {
                fields["client_id"] = clientId;
            }
            else
            {
                // client_secret_basic, which every authorization server supports (RFC 6749 §2.3.1).
                var basic = $"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(clientSecret)}";
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(basic)));
            }
            request.Content = new FormUrlEncodedContent(fields);
            var json = await SendAsync(http, request, "授權伺服器沒有發出 token。", ct);

            var accessToken = Text(json, "access_token");
            var tokenType = Text(json, "token_type");
            if (string.IsNullOrEmpty(accessToken) || !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                throw new McpOAuthException("授權伺服器的回應不是可用的 Bearer token。");
            return new McpOAuthTokenResponse(accessToken, Text(json, "refresh_token"), ExpiresIn(json));
        }

        private HttpClient Client() => new(handlers.Create(), disposeHandler: true) { Timeout = McpOAuthDiscovery.RequestTimeout };

        private static async Task<JsonElement> SendAsync(HttpClient http, HttpRequestMessage request, string failure, CancellationToken ct)
        {
            using (request)
            {
                if (request.RequestUri?.Scheme != Uri.UriSchemeHttps) throw new McpOAuthException(failure);
                request.Headers.Accept.ParseAdd("application/json");
                try
                {
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    var json = await McpOAuthDiscovery.ReadJsonObjectAsync(response, ct);
                    if (response.IsSuccessStatusCode && json != null) return json.Value;
                    // The OAuth error code (RFC 6749 §5.2) is a short word of a fixed alphabet; its description is the server's own text.
                    var code = json is { } e ? Text(e, "error") : null;
                    throw new McpOAuthException(code != null && ErrorCode().IsMatch(code) ? $"{failure}（{code}）" : failure);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is not McpOAuthException)
                {
                    throw new McpOAuthException(ex is BlockedAddressException ? "授權伺服器不是公開的網路位址，基於安全不能連線。" : failure, ex);
                }
            }
        }

        private static string? Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary><c>expires_in</c>, a number of seconds (some servers send it as a string).</summary>
        internal static TimeSpan? ExpiresIn(JsonElement e)
        {
            if (!e.TryGetProperty("expires_in", out var v)) return null;
            long seconds = v.ValueKind switch
            {
                JsonValueKind.Number when v.TryGetInt64(out var n) => n,
                JsonValueKind.String when long.TryParse(v.GetString(), out var s) => s,
                _ => -1,
            };
            return seconds > 0 ? TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.FromDays(366).TotalSeconds)) : null;
        }

        [GeneratedRegex("^[a-z_]{1,40}$")]
        private static partial Regex ErrorCode();
    }

    /// <summary>The site's client metadata document, at its own URL, which is its client id (Client ID Metadata Documents).</summary>
    public static class McpClientMetadata
    {
        public static Dictionary<string, object> Document(McpOAuthUrls urls) => new()
        {
            ["client_id"] = urls.ClientMetadataUrl ?? "",
            ["client_name"] = McpOAuthEndpoints.ClientName,
            ["client_uri"] = Uri.TryCreate(urls.RedirectUri, UriKind.Absolute, out var r) ? r.GetLeftPart(UriPartial.Authority) : "",
            ["redirect_uris"] = new[] { urls.RedirectUri },
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
            ["token_endpoint_auth_method"] = "none",
        };
    }

    /// <summary>The <see cref="McpOAuthUrls"/> of this deployment.</summary>
    public sealed class McpOAuthUrlSource(IOptions<ConnectorOptions> options)
    {
        public McpOAuthUrls Urls => McpOAuthUrls.From(options.Value);
    }
}
