using System.Text.Json;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// A connector's credentials as stored: JSON (<c>{ headerName, value }</c>) encrypted with the app's token
    /// key (<see cref="ITokenEncryptionService"/>, as the account connections' tokens are). They are only
    /// ever sent to the connector's own server, never to the browser or the model.
    /// </summary>
    public sealed class ConnectorSecrets(ITokenEncryptionService encryption, ILogger<ConnectorSecrets> logger)
    {
        private sealed record Stored(string? HeaderName, string? Value);

        /// <summary>The credentials to store, or null for none.</summary>
        public string? Seal(McpAuth auth) =>
            auth.Headers() == null ? null : encryption.EncryptToken(JsonSerializer.Serialize(new Stored(auth.HeaderName, auth.Value)));

        /// <summary>How the connector's requests prove who they are for; none when it has no credentials or they cannot be read.</summary>
        public McpAuth AuthOf(SQL.ConnectorInstallation installation)
        {
            if (string.IsNullOrEmpty(installation.EncryptedSecret)) return McpAuth.None;
            try
            {
                var stored = JsonSerializer.Deserialize<Stored>(encryption.DecryptToken(installation.EncryptedSecret));
                return stored == null ? McpAuth.None : new McpAuth(stored.HeaderName, stored.Value);
            }
            catch (Exception ex)
            {
                // A changed key, or a value from elsewhere: the server will ask for credentials again.
                logger.LogWarning(ex, "Connector {InstallationId}'s credentials could not be read", installation.Id);
                return McpAuth.None;
            }
        }
    }
}
