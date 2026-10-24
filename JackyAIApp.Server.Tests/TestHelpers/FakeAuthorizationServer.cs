using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// The whole web of an MCP server behind an OAuth sign-in (MCP authorization), as the handler of the
    /// connectors' outbound requests, so a sign-in can be tested from probe to refresh with the real
    /// discovery, endpoints and MCP client:
    /// <list type="bullet">
    /// <item>the MCP server (<see cref="Server"/>, a <see cref="FakeMcpServer"/>) answers 401 with
    /// <c>WWW-Authenticate: Bearer resource_metadata=…</c> to any request without a token this issued;</item>
    /// <item>its protected resource metadata (RFC 9728) names the authorization server <see cref="Issuer"/>;</item>
    /// <item>whose metadata (RFC 8414) offers PKCE S256, <c>iss</c> in the response, and dynamic client
    /// registration unless <see cref="Registration"/> is off;</item>
    /// <item><c>/register</c> registers a public client; <c>/token</c> takes an authorization code (checking
    /// the client, the redirect URI, the PKCE verifier and the resource) or a refresh token, which is spent
    /// once used (rotation) and takes the old access token with it.</item>
    /// </list>
    /// The reader's part, signing in at the authorization endpoint, is <see cref="Approve"/>.
    /// </summary>
    public sealed class FakeAuthorizationServer : HttpMessageHandler, IMcpHttpHandlerSource
    {
        public static readonly Uri Server = new("https://mcp.example.com/mcp");
        public const string ResourceMetadataUrl = "https://mcp.example.com/.well-known/oauth-protected-resource/mcp";
        public const string Issuer = "https://auth.example.com";
        public const string AuthorizationEndpoint = Issuer + "/authorize";
        public const string TokenEndpoint = Issuer + "/token";
        public const string RegistrationEndpoint = Issuer + "/register";
        public const string Scope = "notes:read";

        private readonly Dictionary<string, List<string>> _clients = new();
        private readonly Dictionary<string, IssuedCode> _codes = new();
        private readonly HashSet<string> _accessTokens = [];
        // A refresh token, and the access token issued with it.
        private readonly Dictionary<string, string> _refreshTokens = new();
        private int _issued;

        private sealed record IssuedCode(string ClientId, string RedirectUri, string Challenge, string? Resource);

        /// <summary>What the reader is sent back with: the authorization code, the state as it was sent, and <c>iss</c>.</summary>
        public sealed record Redirect(string RedirectUri, string Code, string? State, string Iss);

        public FakeAuthorizationServer()
        {
            McpServer = new FakeMcpServer
            {
                Accepts = request => request.Headers.Authorization is { Scheme: "Bearer", Parameter: { } token } && _accessTokens.Contains(token),
                Challenge = $"Bearer resource_metadata=\"{ResourceMetadataUrl}\", scope=\"{Scope}\"",
            };
        }

        /// <summary>The MCP server behind the sign-in: what it was asked, and with which token.</summary>
        public FakeMcpServer McpServer { get; }

        /// <summary>Whether the authorization server offers dynamic client registration.</summary>
        public bool Registration { get; init; } = true;

        /// <summary>The <c>expires_in</c> of every access token.</summary>
        public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromHours(1);

        /// <summary>The <c>grant_type</c> of every token request it granted, in order.</summary>
        public List<string> TokensGranted { get; } = [];

        /// <summary>The <c>scope</c> of every sign-in the reader approved, in order.</summary>
        public List<string?> ScopesAsked { get; } = [];

        /// <summary>Every URL asked, in order.</summary>
        public List<string> Asked { get; } = [];

        /// <summary>The clients registered, by client id.</summary>
        public IReadOnlyCollection<string> Clients => _clients.Keys;

        public HttpMessageHandler Create() => new KeptAlive(this);

        /// <summary>
        /// The reader signs in at <paramref name="authorizeUrl"/> (the URL the site sent them to) and allows it:
        /// the request is checked as an authorization server would (a registered client and redirect URI, PKCE
        /// S256, this server as the resource) and a code issued. Throws when the request is one a real server refuses.
        /// </summary>
        public Redirect Approve(string authorizeUrl)
        {
            var url = new Uri(authorizeUrl);
            if (url.GetLeftPart(UriPartial.Path) != AuthorizationEndpoint)
                throw new InvalidOperationException($"Not this authorization server's endpoint: {url.GetLeftPart(UriPartial.Path)}");
            var query = HttpUtility.ParseQueryString(url.Query);
            var clientId = query["client_id"] ?? "";
            var redirectUri = query["redirect_uri"] ?? "";
            if (query["response_type"] != "code") throw new InvalidOperationException("response_type is not code");
            if (!_clients.TryGetValue(clientId, out var redirects)) throw new InvalidOperationException($"Unknown client {clientId}");
            if (!redirects.Contains(redirectUri)) throw new InvalidOperationException($"Redirect URI not registered: {redirectUri}");
            if (query["code_challenge_method"] != "S256" || string.IsNullOrEmpty(query["code_challenge"]))
                throw new InvalidOperationException("No PKCE S256 challenge");
            if (query["resource"] != Server.ToString()) throw new InvalidOperationException($"Resource is {query["resource"]}");

            ScopesAsked.Add(query["scope"]);
            var code = $"code-{++_issued}";
            _codes[code] = new IssuedCode(clientId, redirectUri, query["code_challenge"]!, query["resource"]);
            return new Redirect(redirectUri, code, query["state"], Issuer);
        }

        /// <summary>A client registered by hand, as a reader would on the authorization server's own site.</summary>
        public void RegisterClient(string clientId, string redirectUri) => _clients[clientId] = [redirectUri];

        /// <summary>Every token ends, as when the reader takes the app's access away on the server's own site.</summary>
        public void RevokeAll()
        {
            _accessTokens.Clear();
            _refreshTokens.Clear();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            Asked.Add(url);
            if (url == Server.ToString()) return await McpServer.AnswerAsync(request, ct);
            return url switch
            {
                ResourceMetadataUrl when request.Method == HttpMethod.Get => Json(new JsonObject
                {
                    ["resource"] = Server.ToString(),
                    ["authorization_servers"] = new JsonArray(Issuer),
                    ["scopes_supported"] = new JsonArray(Scope),
                }),
                Issuer + "/.well-known/oauth-authorization-server" when request.Method == HttpMethod.Get => Json(Metadata()),
                RegistrationEndpoint when Registration && request.Method == HttpMethod.Post =>
                    Register(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject()),
                TokenEndpoint when request.Method == HttpMethod.Post =>
                    Token(HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(ct))),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private JsonObject Metadata()
        {
            var metadata = new JsonObject
            {
                ["issuer"] = Issuer,
                ["authorization_endpoint"] = AuthorizationEndpoint,
                ["token_endpoint"] = TokenEndpoint,
                ["code_challenge_methods_supported"] = new JsonArray("S256"),
                ["authorization_response_iss_parameter_supported"] = true,
                ["grant_types_supported"] = new JsonArray("authorization_code", "refresh_token"),
            };
            if (Registration) metadata["registration_endpoint"] = RegistrationEndpoint;
            return metadata;
        }

        private HttpResponseMessage Register(JsonObject body)
        {
            var redirects = body["redirect_uris"]?.AsArray().Select(u => u!.GetValue<string>()).ToList() ?? [];
            if (redirects.Count == 0 || body["token_endpoint_auth_method"]?.GetValue<string>() != "none")
                return Error("invalid_client_metadata");
            var clientId = $"client-{_clients.Count + 1}";
            _clients[clientId] = redirects;
            return Json(new JsonObject { ["client_id"] = clientId, ["redirect_uris"] = new JsonArray(redirects.Select(r => (JsonNode?)r).ToArray()) },
                HttpStatusCode.Created);
        }

        private HttpResponseMessage Token(System.Collections.Specialized.NameValueCollection form)
        {
            switch (form["grant_type"])
            {
                case "authorization_code":
                    var code = form["code"] ?? "";
                    if (!_codes.Remove(code, out var issued) || issued.ClientId != form["client_id"] ||
                        issued.RedirectUri != form["redirect_uri"] || issued.Resource != form["resource"] ||
                        ChallengeOf(form["code_verifier"] ?? "") != issued.Challenge)
                        return Error("invalid_grant");
                    TokensGranted.Add("authorization_code");
                    return Tokens();
                case "refresh_token":
                    // Rotation: a refresh token is spent once used, and the access token issued with it ends.
                    if (!_refreshTokens.Remove(form["refresh_token"] ?? "", out var access) || !_clients.ContainsKey(form["client_id"] ?? ""))
                        return Error("invalid_grant");
                    _accessTokens.Remove(access);
                    TokensGranted.Add("refresh_token");
                    return Tokens();
                default:
                    return Error("unsupported_grant_type");
            }
        }

        private HttpResponseMessage Tokens()
        {
            var n = ++_issued;
            var access = $"access-{n}";
            var refresh = $"refresh-{n}";
            _accessTokens.Add(access);
            _refreshTokens[refresh] = access;
            return Json(new JsonObject
            {
                ["access_token"] = access,
                ["token_type"] = "Bearer",
                ["expires_in"] = (long)AccessTokenLifetime.TotalSeconds,
                ["refresh_token"] = refresh,
            });
        }

        private static string ChallengeOf(string verifier) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static HttpResponseMessage Json(JsonObject body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Error(string code) =>
            Json(new JsonObject { ["error"] = code }, HttpStatusCode.BadRequest);
    }
}
