using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// Adding an MCP server of the reader's own and checking it (docs/ai-native/design/03 §5.1, §7): with no
    /// credentials, an API key in a header of theirs, or a bearer token. Signing in with OAuth is not
    /// offered yet (design/07, M3).
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
        AzureSQLDBContext db, ICurrentUser currentUser, IMcpToolClient mcp, ConnectorSecrets secrets, TimeProvider time)
        : IConnectorInstallService
    {
        internal const string OAuthNotYet = "OAuth 登入的 MCP server 還不支援，請改用 API key 或 Bearer token。";

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
                // Without OAuth (M3), a server that wants credentials is given a key or token by hand.
                return ServiceResult.Ok(new McpProbeResultDto(url.ToString(), "apiKey", null, null, null));
            }
            catch (McpServerException ex)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, ex.Message);
            }
        }

        public async Task<ServiceResult<InstalledConnectorDto>> CreateAsync(CreateInstallationRequestDto request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (request.DefinitionKey != SQL.ConnectorInstallation.CustomKey)
                return ServiceResult.Fail(ErrorCodes.NotFound, "目錄裡沒有這個 connector。");

            var (url, urlError) = McpServerUrl.Normalize(request.ServerUrl);
            if (url == null) return ServiceResult.Fail(ErrorCodes.BadRequest, urlError!);

            var (auth, authKind, authError) = AuthFrom(request);
            if (authError != null) return ServiceResult.Fail(ErrorCodes.BadRequest, authError);

            var count = await db.ConnectorInstallations.CountAsync(
                c => c.UserId == userId && c.DefinitionKey == SQL.ConnectorInstallation.CustomKey, ct);
            if (count >= ConnectorCatalogService.CustomLimit)
                return ServiceResult.Fail(ErrorCodes.BadRequest, $"最多可以加 {ConnectorCatalogService.CustomLimit} 個自己的 MCP server。");

            var now = time.GetUtcNow().UtcDateTime;
            var row = new SQL.ConnectorInstallation
            {
                UserId = userId,
                DefinitionKey = SQL.ConnectorInstallation.CustomKey,
                ServerUrl = url.ToString(),
                AuthKind = authKind,
                EncryptedSecret = secrets.Seal(auth),
                CreatedAt = now,
            };
            var (_, blocked) = await RefreshAsync(row, auth, ct);
            // A URL we may not reach is not kept at all.
            if (blocked != null) return ServiceResult.Fail(ErrorCodes.BadRequest, blocked);

            row.DisplayName = Trimmed(request.DisplayName, 100) ?? row.DisplayName ?? url.Host;
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
            var (ok, blocked) = await RefreshAsync(row, secrets.AuthOf(row), ct);
            var latency = (long)time.GetElapsedTime(started).TotalMilliseconds;
            await db.SaveChangesAsync(ct);
            return ServiceResult.Ok(new ConnectionTestResultDto(ok, latency, ok ? null : blocked ?? row.LastError,
                ConnectorCatalogService.InstalledDto(row)));
        }

        /// <summary>
        /// Lists the server's tools into <paramref name="row"/> and records how it went. True when they were
        /// listed; with the reason when the address is one we may not reach.
        /// </summary>
        private async Task<(bool Ok, string? Blocked)> RefreshAsync(SQL.ConnectorInstallation row, McpAuth auth, CancellationToken ct)
        {
            var now = time.GetUtcNow().UtcDateTime;
            row.LastTestedAt = now;
            row.UpdatedAt = now;
            try
            {
                var listed = await mcp.ListToolsAsync(new Uri(row.ServerUrl!), auth, ct);
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
                case "oauth":
                    return (McpAuth.None, "", OAuthNotYet);
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

        private static string? Trimmed(string? text, int max)
        {
            var t = text?.Trim();
            return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
        }
    }
}
