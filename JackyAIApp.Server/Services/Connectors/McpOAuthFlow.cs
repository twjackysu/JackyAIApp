using System.Security.Cryptography;
using System.Text;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>How an MCP sign-in's callback went: the connector's name for the page's message, or an error code.</summary>
    public sealed record McpOAuthCallbackOutcome(bool Ok, string Provider, string? Error);

    /// <summary>
    /// Signing a reader in to an MCP server with OAuth (docs/ai-native/design/03 §5.2): becoming the
    /// authorization server's client when the connector is added, sending the reader there (PKCE S256,
    /// <c>resource</c>, <c>state</c>), and taking the code back for tokens.
    /// </summary>
    public interface IMcpOAuthFlow
    {
        /// <summary>
        /// The sign-in for a new connector, before the reader signed in: the server's authorization server and
        /// the client we are there (by our metadata document, by registering, or the one the reader typed).
        /// Throws <see cref="McpOAuthException"/>.
        /// </summary>
        Task<McpOAuthGrant> PrepareAsync(Uri server, McpCredentialsDto? manual, CancellationToken ct);

        /// <summary>Where to send the reader to sign in to their connector.</summary>
        Task<ServiceResult<AuthorizeRedirectDto>> StartAsync(string installationId, CancellationToken ct);

        /// <summary>The authorization server sent the reader back: checks it, gets the tokens, lists the tools.</summary>
        Task<McpOAuthCallbackOutcome> CompleteAsync(string? code, string? state, string? issuer, string? error, CancellationToken ct);
    }

    public sealed class McpOAuthFlow(
        AzureSQLDBContext db,
        ICurrentUser currentUser,
        IStateService states,
        IMemoryCache cache,
        IMcpOAuthDiscovery discovery,
        IMcpOAuthEndpoints endpoints,
        IMcpToolClient mcp,
        ConnectorSecrets secrets,
        McpOAuthUrlSource urls,
        TimeProvider time,
        ILogger<McpOAuthFlow> logger) : IMcpOAuthFlow
    {
        /// <summary>How long a reader has to sign in; the state says the same (<see cref="StateService"/>).</summary>
        public static readonly TimeSpan PendingFor = TimeSpan.FromMinutes(10);

        /// <summary>What the state names as its provider: the connector it signs in to.</summary>
        internal const string StateProviderPrefix = "mcp:";

        /// <summary>Kept on the server while the reader signs in, never in the browser.</summary>
        internal sealed record Pending(string UserId, string InstallationId, string Verifier);

        public async Task<McpOAuthGrant> PrepareAsync(Uri server, McpCredentialsDto? manual, CancellationToken ct)
        {
            McpAuthChallenge? challenge;
            try
            {
                challenge = await discovery.ChallengeAsync(server, ct);
            }
            catch (McpServerException ex)
            {
                throw new McpOAuthException(ex.Message, ex);
            }
            var found = await discovery.DiscoverAsync(server, challenge ?? new McpAuthChallenge(null, null), ct);
            var site = urls.Urls;

            var typedId = manual?.ClientId?.Trim();
            if (!string.IsNullOrEmpty(typedId))
                return McpOAuthGrant.For(found, typedId, string.IsNullOrWhiteSpace(manual!.ClientSecret) ? null : manual.ClientSecret.Trim());

            switch (site.RegistrationFor(found))
            {
                case "cimd":
                    return McpOAuthGrant.For(found, site.ClientMetadataUrl!, null);
                case "dcr":
                    var (clientId, clientSecret) = await endpoints.RegisterAsync(found.RegistrationEndpoint!, site.RedirectUri, ct);
                    return McpOAuthGrant.For(found, clientId, clientSecret);
                default:
                    throw new McpOAuthException("這個授權伺服器不接受自動註冊，請先在它那邊建立 OAuth app，再填入 client id。");
            }
        }

        public async Task<ServiceResult<AuthorizeRedirectDto>> StartAsync(string installationId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var row = await db.ConnectorInstallations.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == installationId && c.UserId == userId, ct);
            if (row == null) return ServiceResult.Fail(ErrorCodes.NotFound, "找不到這個 connector。");
            var grant = secrets.GrantOf(row);
            if (grant == null) return ServiceResult.Fail(ErrorCodes.BadRequest, "這個 connector 不是用 OAuth 登入的。");

            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var state = states.GenerateState(userId, StateProviderPrefix + row.Id);
            cache.Set(PendingKey(state), new Pending(userId, row.Id, verifier), PendingFor);

            var query = new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = grant.ClientId,
                ["redirect_uri"] = urls.Urls.RedirectUri,
                ["code_challenge"] = Challenge(verifier),
                ["code_challenge_method"] = "S256",
                ["state"] = state,
                ["resource"] = grant.Resource,
                ["scope"] = grant.Scope,
            };
            return ServiceResult.Ok(new AuthorizeRedirectDto(WithQuery(grant.AuthorizationEndpoint, query)));
        }

        public async Task<McpOAuthCallbackOutcome> CompleteAsync(string? code, string? state, string? issuer, string? error, CancellationToken ct)
        {
            // The state is checked first: nothing else in the callback is trusted before it is.
            if (string.IsNullOrEmpty(state) || !states.ValidateState(state, out var userId, out var provider) ||
                !provider.StartsWith(StateProviderPrefix, StringComparison.Ordinal) ||
                cache.Get<Pending>(PendingKey(state)) is not { } pending ||
                pending.UserId != userId || StateProviderPrefix + pending.InstallationId != provider)
                return new McpOAuthCallbackOutcome(false, "MCP", "invalid_request");
            // The browser coming back must be the one whose reader started the sign-in. Otherwise a sign-in URL
            // someone started for their own connector and sent to another person would, once that person
            // approves it, put the other person's account into the sender's connector. Checked before the
            // pending sign-in is used up, and without the connector's name: it is not this reader's.
            if (currentUser.Id != userId)
            {
                logger.LogWarning("MCP sign-in for connector {InstallationId} came back to another reader", pending.InstallationId);
                return new McpOAuthCallbackOutcome(false, "MCP", "wrong_account");
            }
            // Once only.
            cache.Remove(PendingKey(state));

            var row = await db.ConnectorInstallations.FirstOrDefaultAsync(c => c.Id == pending.InstallationId && c.UserId == userId, ct);
            var grant = row == null ? null : secrets.GrantOf(row);
            if (row == null || grant == null) return new McpOAuthCallbackOutcome(false, "MCP", "callback_failed");
            var name = row.DisplayName ?? "MCP";

            if (!string.IsNullOrEmpty(error)) return new McpOAuthCallbackOutcome(false, name, "access_denied");
            if (string.IsNullOrEmpty(code)) return new McpOAuthCallbackOutcome(false, name, "missing_parameters");
            // RFC 9207: the response came from the server the reader was sent to (mix-up attacks).
            if (issuer != null ? !McpOAuthDiscovery.SameUrl(issuer, grant.Issuer) : grant.IssParameterSupported)
            {
                logger.LogWarning("MCP sign-in for connector {InstallationId} came back from another issuer", row.Id);
                return new McpOAuthCallbackOutcome(false, name, "invalid_request");
            }

            try
            {
                var tokens = await endpoints.RequestTokenAsync(new Uri(grant.TokenEndpoint), new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = urls.Urls.RedirectUri,
                    ["code_verifier"] = pending.Verifier,
                    ["resource"] = grant.Resource,
                }, grant.ClientId, grant.ClientSecret, ct);
                // A new sign-in: none of the one before is kept (its refresh token would bring back its narrower scope).
                grant = WithTokens(grant with { RefreshToken = null }, tokens, time.GetUtcNow().UtcDateTime);
            }
            catch (McpOAuthException ex)
            {
                logger.LogWarning(ex, "MCP sign-in for connector {InstallationId} got no token", row.Id);
                row.LastStatus = "NeedsAuth";
                row.LastError = ex.Message;
                row.UpdatedAt = time.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
                return new McpOAuthCallbackOutcome(false, name, "callback_failed");
            }

            row.EncryptedSecret = secrets.Seal(grant);
            await InstallationTools.RefreshAsync(row, McpAuth.BearerToken(grant.AccessToken!), mcp, time, ct);
            await db.SaveChangesAsync(ct);
            return new McpOAuthCallbackOutcome(true, row.DisplayName ?? name, null);
        }

        /// <summary>The grant with a token response's tokens; a refresh that names no new refresh token keeps the old one.</summary>
        internal static McpOAuthGrant WithTokens(McpOAuthGrant grant, McpOAuthTokenResponse tokens, DateTime now) => grant with
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken ?? grant.RefreshToken,
            ExpiresAt = tokens.ExpiresIn is { } lifetime ? now + lifetime : null,
        };

        private static string PendingKey(string state) =>
            "mcp-oauth-pending:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

        /// <summary>The PKCE S256 challenge of a verifier (RFC 7636 §4.2).</summary>
        internal static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>An endpoint with parameters added to any it already has; empty ones left out.</summary>
        internal static string WithQuery(string endpoint, IReadOnlyDictionary<string, string?> parameters)
        {
            var added = string.Join('&', parameters
                .Where(p => !string.IsNullOrEmpty(p.Value))
                .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));
            return endpoint + (endpoint.Contains('?') ? "&" : "?") + added;
        }
    }

    /// <summary>Listing a connector's tools into its row and recording how it went.</summary>
    internal static class InstallationTools
    {
        /// <summary>True when they were listed; with the reason when the address is one we may not reach.</summary>
        public static async Task<(bool Ok, string? Blocked)> RefreshAsync(
            SQL.ConnectorInstallation row, McpAuth auth, IMcpToolClient mcp, TimeProvider time, CancellationToken ct)
        {
            var now = time.GetUtcNow().UtcDateTime;
            row.LastTestedAt = now;
            row.UpdatedAt = now;
            try
            {
                var listed = await mcp.ListToolsAsync(new Uri(row.ServerUrl!), auth, ct);
                AskFirstWhereChanged(row, CachedTools.Changed(CachedTools.Parse(row.ToolsCacheJson), listed.Tools));
                row.ToolsCacheJson = CachedTools.Serialize(listed.Tools);
                row.ToolsFetchedAt = now;
                row.LastStatus = "Ok";
                row.LastError = null;
                row.DisplayName ??= Trimmed(listed.ServerName, 100);
                return (true, null);
            }
            catch (McpServerException ex)
            {
                row.LastStatus = ex.Failure == McpFailure.NeedsAuth ? "NeedsAuth" : "Error";
                row.LastError = ex.Message;
                return (false, ex.Failure == McpFailure.Blocked ? ex.Message : null);
            }
        }

        /// <summary>
        /// A tool the reader let run on its own that now says something else (<see cref="CachedTools.Changed"/>) asks
        /// first again: the reader trusted what it said then, not what it says now.
        /// </summary>
        internal static void AskFirstWhereChanged(SQL.ConnectorInstallation row, IReadOnlyList<string> changed)
        {
            if (changed.Count == 0) return;
            var chosen = ToolPermissions.Parse(row.ToolSettingsJson);
            var trusted = changed.Where(name => chosen.TryGetValue(name, out var p) && p == ToolPermission.Auto).ToList();
            if (trusted.Count == 0) return;
            foreach (var name in trusted) chosen[name] = ToolPermission.Confirm;
            row.ToolSettingsJson = ToolPermissions.Serialize(chosen);
        }

        /// <summary>
        /// A connector's name as kept: what the reader cannot see dropped (a server names itself, and its name is drawn
        /// on the connectors page and in chat cards; <see cref="McpToolClient.Plain"/>, D-236), line breaks and tabs as
        /// spaces, cut to <paramref name="max"/> never between the two halves of a character.
        /// </summary>
        public static string? Trimmed(string? text, int max)
        {
            if (text == null) return null;
            var t = McpToolClient.Plain(text).Replace('\n', ' ').Replace('\t', ' ').Trim();
            if (t.Length == 0) return null;
            return t.Length > max ? t[..(char.IsHighSurrogate(t[max - 1]) ? max - 1 : max)] : t;
        }
    }
}
