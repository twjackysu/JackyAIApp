using System.Collections.Concurrent;
using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>The credentials a request to a connector's server goes with, an OAuth sign-in's refreshed when it is about to expire.</summary>
    public interface IMcpOAuthTokens
    {
        /// <summary>
        /// The connector's credentials (<see cref="ConnectorSecrets.AuthOf"/>); for an OAuth sign-in whose access
        /// token expires within <see cref="McpOAuthTokens.RefreshAhead"/>, a new one first. A refresh the
        /// authorization server refuses (<see cref="McpOAuthException.Refused"/>) marks the connector NeedsAuth and leaves
        /// it without credentials; one it does not answer, for a token already expired, throws
        /// <see cref="McpServerException"/> (<see cref="McpFailure.Unreachable"/>) and keeps the sign-in.
        /// </summary>
        Task<McpAuth> AuthOfAsync(SQL.ConnectorInstallation installation, CancellationToken ct);

        /// <summary>
        /// The connector's server refused its credentials during a call (401/403): it is marked NeedsAuth, so the
        /// connectors page asks the reader to sign in again or update the key. A refusal that named the scope the
        /// call needs (<paramref name="requiredScope"/>, MCP authorization's step-up) adds it to an OAuth sign-in's
        /// scope, so signing in again asks for it; the tokens stay for the calls they are enough for.
        /// </summary>
        Task CredentialsRefusedAsync(string installationId, string? requiredScope, CancellationToken ct);
    }

    /// <summary>
    /// Refreshes one connector at a time (a lock per installation): two requests refreshing together would
    /// spend the refresh token twice, and an authorization server that rotates them would revoke the sign-in.
    /// The one that waited reads the row again and finds the new token. The refresh is its own unit of work
    /// (a context from <see cref="IDbContextFactory{TContext}"/>), not the request's tracked rows.
    /// </summary>
    public sealed class McpOAuthTokens(
        IDbContextFactory<AzureSQLDBContext> dbFactory,
        ConnectorSecrets secrets,
        IMcpOAuthEndpoints endpoints,
        TimeProvider time,
        ILogger<McpOAuthTokens> logger) : IMcpOAuthTokens
    {
        /// <summary>How long before it expires an access token is refreshed.</summary>
        public static readonly TimeSpan RefreshAhead = TimeSpan.FromSeconds(60);

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

        public async Task<McpAuth> AuthOfAsync(SQL.ConnectorInstallation installation, CancellationToken ct)
        {
            var grant = secrets.GrantOf(installation);
            if (grant == null || !IsDue(grant, time.GetUtcNow().UtcDateTime)) return secrets.AuthOf(installation);

            var gate = Locks.GetOrAdd(installation.Id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var row = await db.ConnectorInstallations.FirstOrDefaultAsync(c => c.Id == installation.Id, ct);
                if (row == null) return McpAuth.None;
                grant = secrets.GrantOf(row);
                var now = time.GetUtcNow().UtcDateTime;
                // Refreshed by the request this one waited for.
                if (grant == null || !IsDue(grant, now)) return secrets.AuthOf(row);
                if (string.IsNullOrEmpty(grant.RefreshToken))
                    return grant.ExpiresAt > now ? secrets.AuthOf(row) : await SignedOutAsync(db, row, grant, "登入已過期，請重新授權。", ct);

                try
                {
                    // Not the call's token from here on: an authorization server that rotates refresh tokens has spent the
                    // old one once it answers, so a call stopped (or timed out) between its answer and the save would lose
                    // the sign-in. The request's own timeout (McpOAuthDiscovery.RequestTimeout) still bounds the wait.
                    var tokens = await endpoints.RequestTokenAsync(new Uri(grant.TokenEndpoint), new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = grant.RefreshToken,
                        ["resource"] = grant.Resource,
                    }, grant.ClientId, grant.ClientSecret, CancellationToken.None);
                    grant = McpOAuthFlow.WithTokens(grant, tokens, now);
                    row.EncryptedSecret = secrets.Seal(grant);
                    row.UpdatedAt = now;
                    await db.SaveChangesAsync(CancellationToken.None);
                    return McpAuth.BearerToken(grant.AccessToken!);
                }
                catch (McpOAuthException ex)
                {
                    logger.LogWarning(ex, "Connector {InstallationId}'s sign-in could not be refreshed (refused: {Refused})", row.Id, ex.Refused);
                    // Still valid for a few seconds: use it; the next request tries again.
                    if (grant.ExpiresAt > now) return secrets.AuthOf(row);
                    if (ex.Refused) return await SignedOutAsync(db, row, grant, "登入已過期，請重新授權。", ct);
                    // No answer, or one that may pass (a timeout, 503): the sign-in stays, and the next request tries again.
                    throw new McpServerException(McpFailure.Unreachable, "授權伺服器暫時無法更新登入，請稍後再試。", ex);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task CredentialsRefusedAsync(string installationId, string? requiredScope, CancellationToken ct)
        {
            // The same lock as a refresh: both write the sign-in.
            var gate = Locks.GetOrAdd(installationId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var row = await db.ConnectorInstallations.FirstOrDefaultAsync(c => c.Id == installationId, ct);
                if (row == null) return;
                var grant = secrets.GrantOf(row);
                var stepUp = grant != null && requiredScope != null ? WithScope(grant.Scope, requiredScope) : null;
                if (stepUp != null)
                {
                    row.EncryptedSecret = secrets.Seal(grant! with { Scope = stepUp });
                    row.LastError = "這個動作需要更多權限，請重新授權。";
                }
                else
                {
                    row.LastError = "伺服器不接受目前的認證資料，請重新授權或更新 key。";
                }
                row.LastStatus = "NeedsAuth";
                row.UpdatedAt = time.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>The most scope one sign-in asks for, however many refusals added to it.</summary>
        internal const int MaxScopeLength = 1000;

        /// <summary>
        /// A sign-in's scope with the one a refusal asked for (each scope token once, in order); null when the
        /// refusal named no usable token or the sum would be too long. A scope token is RFC 6749 §3.3's NQCHAR.
        /// </summary>
        internal static string? WithScope(string? granted, string required)
        {
            static IEnumerable<string> Tokens(string? scope) =>
                (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.All(c => c is '!' or (>= '#' and <= '[') or (>= ']' and <= '~')));
            var asked = Tokens(required).ToList();
            if (asked.Count == 0) return null;
            var sum = string.Join(' ', Tokens(granted).Concat(asked).Distinct(StringComparer.Ordinal));
            return sum.Length <= MaxScopeLength ? sum : null;
        }

        internal static bool IsDue(McpOAuthGrant grant, DateTime now) =>
            grant.ExpiresAt is { } expires && expires - RefreshAhead <= now;

        private async Task<McpAuth> SignedOutAsync(
            AzureSQLDBContext db, SQL.ConnectorInstallation row, McpOAuthGrant grant, string why, CancellationToken ct)
        {
            // The client stays (so signing in again needs no new registration); the tokens go.
            row.EncryptedSecret = secrets.Seal(grant with { AccessToken = null, RefreshToken = null, ExpiresAt = null });
            row.LastStatus = "NeedsAuth";
            row.LastError = why;
            row.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return McpAuth.None;
        }
    }
}
