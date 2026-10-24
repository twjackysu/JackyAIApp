using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// Adding a catalog MCP server (<see cref="BuiltInMcpConnectors"/>) or one of the reader's own, and checking it (docs/ai-native/design/03 §5, §7): with no
    /// credentials, an API key in a header of theirs, a bearer token, or an OAuth sign-in (<see cref="IMcpOAuthFlow"/>:
    /// the connector is saved as NeedsAuth and the reader is then sent to sign in).
    /// </summary>
    public interface IConnectorInstallService
    {
        /// <summary>Looks at a URL before anything is saved: is it one we may reach, and does it want credentials.</summary>
        Task<ServiceResult<McpProbeResultDto>> ProbeAsync(string? serverUrl, CancellationToken ct);

        /// <summary>Saves the server and lists its tools once; a server that cannot be listed is saved with why.</summary>
        Task<ServiceResult<InstalledConnectorDto>> CreateAsync(CreateInstallationRequestDto request, CancellationToken ct);

        /// <summary>Lists the server's tools again, and keeps them.</summary>
        Task<ServiceResult<ConnectionTestResultDto>> TestAsync(string installationId, CancellationToken ct);
    }

    public sealed class ConnectorInstallService(
        AzureSQLDBContext db,
        ICurrentUser currentUser,
        IMcpToolClient mcp,
        ConnectorSecrets secrets,
        IMcpOAuthDiscovery discovery,
        IMcpOAuthFlow oauth,
        IMcpOAuthTokens tokens,
        McpOAuthUrlSource urls,
        TimeProvider time)
        : IConnectorInstallService
    {

        public async Task<ServiceResult<McpProbeResultDto>> ProbeAsync(string? serverUrl, CancellationToken ct)
        {
            var (url, error) = McpServerUrl.Normalize(serverUrl);
            if (url == null) return ServiceResult.Fail(ErrorCodes.BadRequest, error!);
            try
            {
                var listed = await mcp.ListToolsAsync(url, McpAuth.None, ct);
                return ServiceResult.Ok(new McpProbeResultDto(url.ToString(), "none", null, null, listed.ServerName));
            }
            catch (McpServerException ex) when (ex.Failure == McpFailure.NeedsAuth)
            {
                return ServiceResult.Ok(await SignInOfAsync(url, ct));
            }
            catch (McpServerException ex)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, ex.Message);
            }
        }

        public async Task<ServiceResult<InstalledConnectorDto>> CreateAsync(CreateInstallationRequestDto request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var custom = request.DefinitionKey == SQL.ConnectorInstallation.CustomKey;
            var builtIn = custom ? null : BuiltInMcpConnectors.Find(request.DefinitionKey);
            if (!custom && builtIn == null)
                return ServiceResult.Fail(ErrorCodes.NotFound, "目錄裡沒有這個 connector。");

            // A catalog entry's server is the catalog's, whatever the request says.
            var (url, urlError) = McpServerUrl.Normalize(builtIn?.Url ?? request.ServerUrl);
            if (url == null) return ServiceResult.Fail(ErrorCodes.BadRequest, urlError!);

            var signIn = request.AuthKind == "oauth";
            var (auth, authKind, authError) = signIn ? (McpAuth.None, "OAuth", null) : AuthFrom(request);
            if (authError != null) return ServiceResult.Fail(ErrorCodes.BadRequest, authError);

            if (builtIn != null)
            {
                if (await db.ConnectorInstallations.AnyAsync(c => c.UserId == userId && c.DefinitionKey == builtIn.Key, ct))
                    return ServiceResult.Fail(ErrorCodes.BadRequest, "這個 connector 已經加過了。");
            }
            else if (await db.ConnectorInstallations.CountAsync(
                         c => c.UserId == userId && c.DefinitionKey == SQL.ConnectorInstallation.CustomKey, ct)
                     >= ConnectorCatalogService.CustomLimit)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, $"最多可以加 {ConnectorCatalogService.CustomLimit} 個自己的 MCP server。");
            }

            var now = time.GetUtcNow().UtcDateTime;
            var row = new SQL.ConnectorInstallation
            {
                UserId = userId,
                DefinitionKey = builtIn?.Key ?? SQL.ConnectorInstallation.CustomKey,
                ServerUrl = url.ToString(),
                AuthKind = authKind,
                EncryptedSecret = secrets.Seal(auth),
                CreatedAt = now,
            };
            if (signIn)
            {
                // Worked out on the server again, whatever the probe told the page.
                try
                {
                    row.EncryptedSecret = secrets.Seal(await oauth.PrepareAsync(url, request.Credentials, ct));
                }
                catch (McpOAuthException ex)
                {
                    return ServiceResult.Fail(ErrorCodes.BadRequest, ex.Message);
                }
                row.LastStatus = "NeedsAuth";
                row.UpdatedAt = now;
            }
            else
            {
                var (_, blocked) = await InstallationTools.RefreshAsync(row, auth, mcp, time, ct);
                // A URL we may not reach is not kept at all.
                if (blocked != null) return ServiceResult.Fail(ErrorCodes.BadRequest, blocked);
            }

            row.DisplayName = builtIn?.DisplayName ?? InstallationTools.Trimmed(request.DisplayName, 100) ?? row.DisplayName ?? url.Host;
            db.ConnectorInstallations.Add(row);
            await db.SaveChangesAsync(ct);
            return ServiceResult.Ok(ConnectorCatalogService.InstalledDto(row));
        }

        public async Task<ServiceResult<ConnectionTestResultDto>> TestAsync(string installationId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var row = await db.ConnectorInstallations.FirstOrDefaultAsync(c => c.Id == installationId && c.UserId == userId, ct);
            if (row?.ServerUrl == null) return ServiceResult.Fail(ErrorCodes.NotFound, "找不到這個 connector。");

            var started = time.GetTimestamp();
            var (ok, blocked) = await InstallationTools.RefreshAsync(row, await tokens.AuthOfAsync(row, ct), mcp, time, ct);
            var latency = (long)time.GetElapsedTime(started).TotalMilliseconds;
            await db.SaveChangesAsync(ct);
            return ServiceResult.Ok(new ConnectionTestResultDto(ok, latency, ok ? null : blocked ?? row.LastError,
                ConnectorCatalogService.InstalledDto(row)));
        }

        /// <summary>
        /// What a server that answered 401 wants: an OAuth sign-in when its metadata leads to an authorization
        /// server we can use, else a key or token typed by hand.
        /// </summary>
        private async Task<McpProbeResultDto> SignInOfAsync(Uri url, CancellationToken ct)
        {
            try
            {
                var challenge = await discovery.ChallengeAsync(url, ct);
                if (challenge != null)
                {
                    var found = await discovery.DiscoverAsync(url, challenge, ct);
                    return new McpProbeResultDto(url.ToString(), "oauth", new Uri(found.Issuer).Host, urls.Urls.RegistrationFor(found), null);
                }
            }
            catch (Exception ex) when (ex is McpOAuthException or McpServerException)
            {
                // No sign-in we can use: a key or token, as before.
            }
            return new McpProbeResultDto(url.ToString(), "apiKey", null, null, null);
        }

        /// <summary>The credentials the request gives, as stored and sent; or why they cannot be used.</summary>
        internal static (McpAuth Auth, string AuthKind, string? Error) AuthFrom(CreateInstallationRequestDto request)
        {
            var value = request.Credentials?.Value?.Trim();
            switch (request.AuthKind)
            {
                case "none":
                    return (McpAuth.None, "None", null);
                case "bearer":
                    if (string.IsNullOrEmpty(value)) return (McpAuth.None, "", "請輸入 token。");
                    if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) value = value["Bearer ".Length..].Trim();
                    return IsHeaderValue(value)
                        ? (new McpAuth("Authorization", $"Bearer {value}"), "Bearer", null)
                        : (McpAuth.None, "", "token 含有不能放在 HTTP header 的字元。");
                case "apiKey":
                    var header = request.Credentials?.HeaderName?.Trim();
                    if (string.IsNullOrEmpty(header) || !IsHeaderName(header)) return (McpAuth.None, "", "請輸入正確的 header 名稱（例如 X-API-Key）。");
                    if (string.IsNullOrEmpty(value)) return (McpAuth.None, "", "請輸入 API key。");
                    return IsHeaderValue(value)
                        ? (new McpAuth(header, value), "ApiKey", null)
                        : (McpAuth.None, "", "API key 含有不能放在 HTTP header 的字元。");
                default:
                    return (McpAuth.None, "", "不認得的登入方式。");
            }
        }

        // RFC 9110 token characters; a few names are the transport's own and are not the reader's to set.
        private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Host", "Content-Type", "Content-Length", "Accept", "Mcp-Session-Id", "Mcp-Protocol-Version", "Cookie", "Transfer-Encoding",
        };

        private static bool IsHeaderName(string name) =>
            name.Length <= 100 && !ReservedHeaders.Contains(name) &&
            name.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c));

        private static bool IsHeaderValue(string value) =>
            value.Length <= 4000 && value.All(c => c is >= ' ' and <= '~');
    }
}
