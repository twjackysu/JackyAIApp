using System.Text.Json;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// A connector's credentials as stored: JSON encrypted with the app's token key
    /// (<see cref="ITokenEncryptionService"/>, as the account connections' tokens are) — a header and its value
    /// (<c>{ headerName, value }</c>), or an OAuth sign-in (<c>{ oauth: … }</c>, <see cref="McpOAuthGrant"/>).
    /// They are only ever sent to the connector's own servers, never to the browser or the model.
    /// </summary>
    public sealed class ConnectorSecrets(ITokenEncryptionService encryption, ILogger<ConnectorSecrets> logger)
    {
        private sealed record Stored(string? HeaderName, string? Value, McpOAuthGrant? OAuth = null);

        /// <summary>The credentials to store, or null for none.</summary>
        public string? Seal(McpAuth auth) =>
            auth.Headers() == null ? null : encryption.EncryptToken(JsonSerializer.Serialize(new Stored(auth.HeaderName, auth.Value)));

        /// <summary>An OAuth sign-in to store.</summary>
        public string Seal(McpOAuthGrant grant) => encryption.EncryptToken(JsonSerializer.Serialize(new Stored(null, null, grant)));

        /// <summary>
        /// How the connector's requests prove who they are for: its header, or its OAuth access token as a
        /// bearer token (as stored: <see cref="IMcpOAuthTokens"/> refreshes one about to expire); none when it
        /// has no credentials or they cannot be read.
        /// </summary>
        public McpAuth AuthOf(SQL.ConnectorInstallation installation)
        {
            var stored = Open(installation);
            if (stored?.OAuth is { } grant)
                return string.IsNullOrEmpty(grant.AccessToken) ? McpAuth.None : McpAuth.BearerToken(grant.AccessToken);
            return stored == null ? McpAuth.None : new McpAuth(stored.HeaderName, stored.Value);
        }

        /// <summary>The connector's OAuth sign-in; null when it has none or it cannot be read.</summary>
        public McpOAuthGrant? GrantOf(SQL.ConnectorInstallation installation) => Open(installation)?.OAuth;

        private Stored? Open(SQL.ConnectorInstallation installation)
        {
            if (string.IsNullOrEmpty(installation.EncryptedSecret)) return null;
            try
            {
                return JsonSerializer.Deserialize<Stored>(encryption.DecryptToken(installation.EncryptedSecret));
            }
            catch (Exception ex)
            {
                // A changed key, or a value from elsewhere: the server will ask for credentials again.
                logger.LogWarning(ex, "Connector {InstallationId}'s credentials could not be read", installation.Id);
                return null;
            }
        }
    }
}
