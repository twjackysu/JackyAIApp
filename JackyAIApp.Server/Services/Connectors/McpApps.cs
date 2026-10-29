using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// The MCP Apps of the reader's connectors, for the chat to draw (docs/ai-native/design/09 §3): the HTML of one
    /// <c>ui://</c> resource a tool of theirs named, read from that connector's own server with their credentials.
    /// </summary>
    public interface IMcpAppService
    {
        /// <summary>
        /// The app <paramref name="uri"/> of the reader's connector <paramref name="installationId"/>: NotFound for someone
        /// else's connector, one without a server, or a URI none of its tools named; a failure when the server has no such app.
        /// </summary>
        Task<ServiceResult<McpAppDto>> GetAsync(string installationId, string? uri, CancellationToken ct);

        /// <summary>
        /// A call one of the connector's apps asks of its server (design/09 §3): only a tool of that connector the apps may call
        /// (<see cref="CachedTool.NotForApp"/> not set), that only reads, and that the reader lets run on its own — no model and
        /// no confirmation card are in between, so nothing that changes anything. Its result goes to the app alone.
        /// </summary>
        Task<ServiceResult<McpAppCallResultDto>> CallAsync(string installationId, string? tool, JsonElement arguments, CancellationToken ct);
    }

    public sealed class McpAppService(
        AzureSQLDBContext db, ICurrentUser currentUser, IMcpToolClient mcp, IMcpOAuthTokens tokens, ILogger<McpAppService> logger) : IMcpAppService
    {
        private const string NotForAppMessage = "這個介面不能自己執行這個工具；請在對話裡請 AI 幫你做。";

        public async Task<ServiceResult<McpAppCallResultDto>> CallAsync(
            string installationId, string? tool, JsonElement arguments, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var row = await db.ConnectorInstallations.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == installationId && c.UserId == userId, ct);
            if (row?.ServerUrl == null) return ServiceResult.Fail(ErrorCodes.NotFound, "找不到這個 connector。");
            var tools = CachedTools.Parse(row.ToolsCacheJson);
            var called = tools.FirstOrDefault(t => string.Equals(t.Name, tool, StringComparison.Ordinal));
            if (called is null) return ServiceResult.Fail(ErrorCodes.Forbidden, NotForAppMessage);
            // Only a connector with an app has apps to ask, and only a tool they may call that the reader lets read on its own.
            if (!row.Enabled || called.NotForApp || called.Effect != ToolEffect.Read ||
                !tools.Any(t => t.UiResourceUri != null) ||
                ToolPermissions.For(called.Name, called.Effect, ToolPermissions.Parse(row.ToolSettingsJson)) != ToolPermission.Auto)
                return ServiceResult.Fail(ErrorCodes.Forbidden, NotForAppMessage);

            var connector = row.DisplayName ?? row.DefinitionKey;
            try
            {
                var outcome = await mcp.CallToolAsync(new Uri(row.ServerUrl), await tokens.AuthOfAsync(row, ct), called.Name,
                    arguments.ValueKind == JsonValueKind.Object ? arguments : JsonSerializer.SerializeToElement(new { }), ct);
                if (outcome.AppResultJson == null)
                    return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"「{connector}」的回應太大，介面沒有收到。");
                return ServiceResult.Ok(new McpAppCallResultDto(JsonSerializer.Deserialize<JsonElement>(outcome.AppResultJson)));
            }
            catch (McpServerException ex)
            {
                logger.LogWarning(ex, "MCP app call {Tool} of connector {InstallationId} failed ({Failure})", called.Name, row.Id, ex.Failure);
                if (ex.Failure == McpFailure.NeedsAuth) await tokens.CredentialsRefusedAsync(row.Id, ex.RequiredScope, CancellationToken.None);
                return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"「{connector}」目前無法使用：{ex.Message}");
            }
        }

        public async Task<ServiceResult<McpAppDto>> GetAsync(string installationId, string? uri, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var row = await db.ConnectorInstallations.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == installationId && c.UserId == userId, ct);
            var appUri = McpToolClient.AppUriOf(uri);
            // Only a connector the reader has on, and a URI among the first few its tools named: the reader's credentials go
            // to read what the chat may draw, nothing else, and no server can make the site read a hundred apps (D-202).
            if (row?.ServerUrl == null || !row.Enabled || appUri == null || !AppUris(row.ToolsCacheJson).Contains(appUri))
                return ServiceResult.Fail(ErrorCodes.NotFound, "找不到這個介面。");

            var server = new Uri(row.ServerUrl);
            var connector = row.DisplayName ?? row.DefinitionKey;
            try
            {
                // Not cached here (D-202): an app's HTML may be half a megabyte, and the client keeps it for its page.
                var app = await mcp.ReadAppAsync(server, await tokens.AuthOfAsync(row, ct), appUri, ct);
                if (app == null) return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"「{connector}」沒有提供可以顯示的介面。");
                return ServiceResult.Ok(new McpAppDto(app.Html, app.ConnectDomains, app.ResourceDomains, app.PrefersBorder,
                    connector, server.IdnHost));
            }
            catch (McpServerException ex)
            {
                logger.LogWarning(ex, "MCP app {Uri} of connector {InstallationId} could not be read ({Failure})", appUri, row.Id, ex.Failure);
                if (ex.Failure == McpFailure.NeedsAuth) await tokens.CredentialsRefusedAsync(row.Id, ex.RequiredScope, CancellationToken.None);
                return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"「{connector}」的介面目前無法讀取：{ex.Message}");
            }
        }

        /// <summary>The most different apps of one connector drawn; a tool naming another is shown as text.</summary>
        public const int MaxAppsPerConnector = 5;

        /// <summary>The first <see cref="MaxAppsPerConnector"/> different apps the connector's tools named, in their order.</summary>
        internal static IReadOnlyList<string> AppUris(string? toolsCacheJson) =>
            CachedTools.Parse(toolsCacheJson).Select(t => t.UiResourceUri).OfType<string>()
                .Distinct(StringComparer.Ordinal).Take(MaxAppsPerConnector).ToList();
    }
}
