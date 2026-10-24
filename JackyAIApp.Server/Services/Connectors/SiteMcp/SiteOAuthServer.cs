using System.Text.Json.Nodes;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.DTO;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services.Connectors.SiteMcp
{
    /// <summary>
    /// The site's addresses as the authorization server of its own MCP server, all from the origin of
    /// <c>Connectors:CallbackBaseUrl</c> (the site's public address; the request's own origin when it is not set, as on a
    /// developer's machine).
    /// </summary>
    public sealed record SiteOAuthUrls(string Issuer)
    {
        public string Resource => Issuer + "/api/mcp";
        public string ResourceMetadata => Issuer + "/.well-known/oauth-protected-resource/api/mcp";
        public string AuthorizationEndpoint => Issuer + "/oauth/authorize";
        public string TokenEndpoint => Issuer + "/api/oauth/token";
        public string RegistrationEndpoint => Issuer + "/api/oauth/register";

        public static SiteOAuthUrls From(ConnectorOptions options, string requestOrigin) =>
            new(Uri.TryCreate(options.CallbackBaseUrl, UriKind.Absolute, out var callback)
                ? callback.GetLeftPart(UriPartial.Authority)
                : requestOrigin.TrimEnd('/'));
    }

    /// <summary>An OAuth answer: its status and JSON body, in OAuth's own shape (not the app's envelope).</summary>
    public sealed record SiteOAuthReply(int StatusCode, JsonObject Body)
    {
        public static SiteOAuthReply Error(string error, string description, int status = StatusCodes.Status400BadRequest) =>
            new(status, new JsonObject { ["error"] = error, ["error_description"] = description });
    }

    /// <summary>
    /// The site as an OAuth 2.1 authorization server for its own MCP server (design/03 §9.1), so that an AI signing in
    /// to MCP servers with OAuth (Claude.ai, ChatGPT, and any client following the MCP authorization spec) can use the
    /// site's tools in a reader's name: protected resource metadata (RFC 9728), authorization server metadata (RFC 8414),
    /// dynamic client registration (RFC 7591, <see cref="SiteOAuthClients"/>), the authorization code flow with PKCE S256
    /// and the reader's consent on the site's own page, and a token that is one of the reader's personal access tokens,
    /// named after the client, listed and revocable on the connectors page. No refresh token: when it ends after
    /// <see cref="SiteOAuthServer.TokenLifetimeDays"/>, the client signs the reader in again.
    /// </summary>
    public interface ISiteOAuthServer
    {
        JsonObject ProtectedResourceMetadata(SiteOAuthUrls urls);
        JsonObject AuthorizationServerMetadata(SiteOAuthUrls urls);
        SiteOAuthReply Register(SiteOAuthRegisterRequestDto request);

        /// <summary>What the consent page shows; a request that cannot be answered by a redirect is a failure shown there.</summary>
        ServiceResult<SiteOAuthConsentDto> Consent(SiteOAuthAuthorizeRequestDto request, SiteOAuthUrls urls);

        /// <summary>The reader's answer: back to the client with a code, or with <c>access_denied</c>.</summary>
        ServiceResult<SiteOAuthRedirectDto> Decide(SiteOAuthDecisionDto decision, SiteOAuthUrls urls);

        Task<SiteOAuthReply> TokenAsync(SiteOAuthTokenRequestDto request, SiteOAuthUrls urls, CancellationToken ct);
    }

    public sealed class SiteOAuthServer(
        SiteOAuthClients clients, SiteOAuthCodes codes, ISiteAccessTokenService tokens, ICurrentUser currentUser) : ISiteOAuthServer
    {
        public const string ReadScope = "mcp:read";
        public const string WriteScope = "mcp:write";
        public const int TokenLifetimeDays = 90;

        public JsonObject ProtectedResourceMetadata(SiteOAuthUrls urls) => new()
        {
            ["resource"] = urls.Resource,
            ["authorization_servers"] = new JsonArray(urls.Issuer),
            ["scopes_supported"] = new JsonArray(ReadScope, WriteScope),
            ["bearer_methods_supported"] = new JsonArray("header"),
            ["resource_name"] = SiteMcpServer.ServerTitle,
        };

        public JsonObject AuthorizationServerMetadata(SiteOAuthUrls urls) => new()
        {
            ["issuer"] = urls.Issuer,
            ["authorization_endpoint"] = urls.AuthorizationEndpoint,
            ["token_endpoint"] = urls.TokenEndpoint,
            ["registration_endpoint"] = urls.RegistrationEndpoint,
            ["scopes_supported"] = new JsonArray(ReadScope, WriteScope),
            ["response_types_supported"] = new JsonArray("code"),
            ["grant_types_supported"] = new JsonArray("authorization_code"),
            ["token_endpoint_auth_methods_supported"] = new JsonArray("none"),
            ["code_challenge_methods_supported"] = new JsonArray("S256"),
            ["authorization_response_iss_parameter_supported"] = true,
        };

        public SiteOAuthReply Register(SiteOAuthRegisterRequestDto request)
        {
            if (request.TokenEndpointAuthMethod is { } method && method != "none")
                return SiteOAuthReply.Error("invalid_client_metadata", "Only public clients (token_endpoint_auth_method none, with PKCE) are supported.");
            var uris = request.RedirectUris ?? [];
            if (clients.Register(request.ClientName, uris) is not { } clientId)
                return SiteOAuthReply.Error("invalid_redirect_uri", "Redirect URIs must be https, or http to localhost (at most 10).");

            var client = clients.Read(clientId)!;
            var registered = new JsonArray();
            foreach (var uri in client.RedirectUris) registered.Add(uri);
            return new SiteOAuthReply(StatusCodes.Status201Created, new JsonObject
            {
                ["client_id"] = clientId,
                ["client_name"] = client.Name,
                ["redirect_uris"] = registered,
                ["token_endpoint_auth_method"] = "none",
                ["grant_types"] = new JsonArray("authorization_code"),
                ["response_types"] = new JsonArray("code"),
            });
        }

        /// <summary>A checked request: who asks, where to, and with what.</summary>
        private sealed record Checked(SiteOAuthClient Client, string ClientId, string RedirectUri, string Challenge, bool WantsWrites);

        /// <summary>
        /// Checks a request. Until the client and its redirect URI check out, nothing may be sent there (an open redirect),
        /// so those failures are shown on the site; the rest of OAuth's errors are too, since the reader is on our page.
        /// </summary>
        private static ServiceResult<Checked> Check(SiteOAuthAuthorizeRequestDto r, SiteOAuthUrls urls, SiteOAuthClients clients)
        {
            static ServiceFailure Bad(string message) => ServiceResult.Fail(ErrorCodes.BadRequest, message);

            if (clients.Read(r.ClientId) is not { } client) return Bad("這個 AI 的登入要求無效（不認得的 client），請回到它重新連線。");
            if (r.RedirectUri is not { } redirect || !client.RedirectUris.Contains(redirect, StringComparer.Ordinal))
                return Bad("這個登入要求的返回網址不在它註冊的網址裡，為了安全不會繼續。");
            if (r.ResponseType != "code") return Bad("這個登入要求不是授權碼流程（response_type=code）。");
            if (r.CodeChallengeMethod != "S256" || !Pkce.IsS256Challenge(r.CodeChallenge))
                return Bad("這個登入要求沒有使用 PKCE（S256），為了安全不會繼續。");
            if (r.Resource is { Length: > 0 } resource && resource.TrimEnd('/') != urls.Resource)
                return Bad("這個登入要求不是給本站的 MCP server。");
            var scopes = (r.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return ServiceResult.Ok(new Checked(client, r.ClientId!, redirect, r.CodeChallenge!, scopes.Contains(WriteScope)));
        }

        public ServiceResult<SiteOAuthConsentDto> Consent(SiteOAuthAuthorizeRequestDto request, SiteOAuthUrls urls)
        {
            currentUser.RequireId();
            var check = Check(request, urls, clients);
            if (!check.IsSuccess) return check.AsFailure();
            var c = check.Data;
            return ServiceResult.Ok(new SiteOAuthConsentDto(c.Client.Name, SiteOAuthRedirects.HostOf(c.RedirectUri), c.WantsWrites));
        }

        public ServiceResult<SiteOAuthRedirectDto> Decide(SiteOAuthDecisionDto decision, SiteOAuthUrls urls)
        {
            var userId = currentUser.RequireId();
            var check = Check(decision.Request, urls, clients);
            if (!check.IsSuccess) return check.AsFailure();
            var c = check.Data;

            var answer = new Dictionary<string, string?>();
            if (decision.Approve)
                answer["code"] = codes.Issue(new SiteOAuthGrant(userId, c.ClientId, c.Client.Name, c.RedirectUri, c.Challenge, decision.AllowWrites));
            else
                answer["error"] = "access_denied";
            if (decision.Request.State is { } state) answer["state"] = state;
            answer["iss"] = urls.Issuer; // RFC 9207
            return ServiceResult.Ok(new SiteOAuthRedirectDto(QueryHelpers.AddQueryString(c.RedirectUri, answer)));
        }

        public async Task<SiteOAuthReply> TokenAsync(SiteOAuthTokenRequestDto request, SiteOAuthUrls urls, CancellationToken ct)
        {
            if (request.GrantType != "authorization_code")
                return SiteOAuthReply.Error("unsupported_grant_type", "Only authorization_code is supported.");
            // Redeemed before anything else is checked: a code is spent by any attempt, so a stolen one cannot be tried twice.
            if (codes.Redeem(request.Code) is not { } grant)
                return SiteOAuthReply.Error("invalid_grant", "The code is not valid, was used, or expired.");
            if (request.ClientId != grant.ClientId || request.RedirectUri != grant.RedirectUri)
                return SiteOAuthReply.Error("invalid_grant", "The code was issued to another client or redirect URI.");
            if (!Pkce.Verifies(request.CodeVerifier, grant.CodeChallenge))
                return SiteOAuthReply.Error("invalid_grant", "The code verifier does not match.");
            if (request.Resource is { Length: > 0 } resource && resource.TrimEnd('/') != urls.Resource)
                return SiteOAuthReply.Error("invalid_target", "This server issues tokens for its MCP server only.");

            var issued = await tokens.IssueAsync(grant.UserId, grant.ClientName, grant.AllowWrites, TokenLifetimeDays, ct);
            if (issued == null)
                return SiteOAuthReply.Error("invalid_grant", $"The reader has {SiteAccessTokenService.Limit} tokens already; one must be revoked on the connectors page first.");

            return new SiteOAuthReply(StatusCodes.Status200OK, new JsonObject
            {
                ["access_token"] = issued,
                ["token_type"] = "Bearer",
                ["expires_in"] = TokenLifetimeDays * 24 * 60 * 60,
                ["scope"] = grant.AllowWrites ? $"{ReadScope} {WriteScope}" : ReadScope,
            });
        }
    }
}
