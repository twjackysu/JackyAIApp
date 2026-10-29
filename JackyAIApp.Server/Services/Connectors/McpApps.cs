using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

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
    }

    public sealed class McpAppService(
        AzureSQLDBContext db, ICurrentUser currentUser, IMcpToolClient mcp, IMcpOAuthTokens tokens, IMemoryCache cache,
        ILogger<McpAppService> logger) : IMcpAppService
    {
        /// <summary>How long an app's HTML is kept for its connector: a conversation's cards ask for it again and again.</summary>
        public static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(10);

        public async Task<ServiceResult<McpAppDto>> GetAsync(string installationId, string? uri, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var row = await db.ConnectorInstallations.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == installationId && c.UserId == userId, ct);
            var appUri = McpToolClient.AppUriOf(uri);
            // Only a URI one of its tools named: the reader's credentials go to read what the chat may draw, nothing else.
            if (row?.ServerUrl == null || appUri == null ||
                !CachedTools.Parse(row.ToolsCacheJson).Any(t => string.Equals(t.UiResourceUri, appUri, StringComparison.Ordinal)))
                return ServiceResult.Fail(ErrorCodes.NotFound, "找不到這個介面。");

            var server = new Uri(row.ServerUrl);
            var connector = row.DisplayName ?? row.DefinitionKey;
            try
            {
                var auth = await tokens.AuthOfAsync(row, ct);
                // Shared by every request for it: the factory uses no request's token, and the credentials are already read.
                var app = await cache.GetOrCreateSingleFlightAsync(
                    $"mcp-app:{row.Id}:{appUri}", CacheTime,
                    () => mcp.ReadAppAsync(server, auth, appUri, CancellationToken.None), ct);
                if (app == null) return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"「{connector}」沒有提供可以顯示的介面。");
                return ServiceResult.Ok(new McpAppDto(app.Html, app.ConnectDomains, app.ResourceDomains, app.PrefersBorder,
                    connector, server.IdnHost));
            }
            catch (McpServerException ex)
            {
                logger.LogWarning(ex, "MCP app {Uri} of connector {InstallationId} could not be read ({Failure})", appUri, row.Id, ex.Failure);
                return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"「{connector}」的介面目前無法讀取：{ex.Message}");
            }
        }
    }
}
