using System.Text.Json;
using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>The tools of the reader's own connectors, for an assistant that offers them (docs/ai-native/design/03).</summary>
    public interface IConnectorToolSource
    {
        /// <summary>
        /// Every tool of every connector the reader has turned on (the tools each last listed), as tools the
        /// server runs; none they turned off. Each tool's connector (<see cref="AiToolDescriptor.Connector"/>)
        /// is its installation's id, so its permission is the reader's choice there.
        /// </summary>
        Task<IReadOnlyList<IAiTool>> ForUserAsync(string userId, CancellationToken ct);

        /// <summary>
        /// The address of every MCP server the reader has added, turned on or not, as <see cref="McpServerUrl.Key"/>
        /// writes it: so Studio AI's search of the registry can tell one they already have (D-198).
        /// </summary>
        Task<IReadOnlySet<string>> ServerUrlsAsync(string userId, CancellationToken ct);
    }

    public sealed class ConnectorToolSource(
        AzureSQLDBContext db, IMcpToolClient mcp, IMcpOAuthTokens tokens, ILogger<ConnectorToolSource> logger) : IConnectorToolSource
    {
        /// <summary>The most connectors' tools one turn is offered.</summary>
        public const int MaxConnectors = 10;

        public async Task<IReadOnlySet<string>> ServerUrlsAsync(string userId, CancellationToken ct)
        {
            var urls = await db.ConnectorInstallations.AsNoTracking()
                .Where(c => c.UserId == userId && c.ServerUrl != null)
                .Select(c => c.ServerUrl!)
                .ToListAsync(ct);
            return urls.Select(McpServerUrl.Key).OfType<string>().ToHashSet(StringComparer.Ordinal);
        }

        public async Task<IReadOnlyList<IAiTool>> ForUserAsync(string userId, CancellationToken ct)
        {
            var rows = await db.ConnectorInstallations.AsNoTracking()
                .Where(c => c.UserId == userId && c.Enabled && c.ServerUrl != null && c.ToolsCacheJson != null)
                .OrderBy(c => c.CreatedAt)
                .Take(MaxConnectors)
                .ToListAsync(ct);

            var tools = new List<IAiTool>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var chosen = ToolPermissions.Parse(row.ToolSettingsJson);
                // A short key of its own per connector in the turn: the model sees "m1__search", never a
                // name a server could pick to pass its tool off as the site's.
                var shortKey = $"m{i + 1}";
                foreach (var tool in CachedTools.Parse(row.ToolsCacheJson))
                {
                    if (ToolPermissions.For(tool.Name, tool.Effect, chosen) == ToolPermission.Disabled) continue;
                    var connectorTool = new McpConnectorTool(row, shortKey, tool, mcp, tokens, logger);
                    // A server listing a name twice, or two that read the same once made API-safe ("files.read",
                    // "files_read"): only the first, since the API refuses a request naming a tool twice.
                    if (!names.Add(connectorTool.Descriptor.Name)) continue;
                    tools.Add(connectorTool);
                }
            }
            return tools;
        }
    }

    /// <summary>One tool of a reader's MCP server, as the chat runs it.</summary>
    internal sealed class McpConnectorTool(
        SQL.ConnectorInstallation installation, string shortKey, CachedTool tool, IMcpToolClient mcp, IMcpOAuthTokens tokens,
        ILogger logger) : IAiTool
    {
        private readonly string _connectorName = installation.DisplayName ?? installation.DefinitionKey;

        public AiToolDescriptor Descriptor { get; } = new(
            ToolNames.ForModel(shortKey, tool.Name),
            $"{installation.DisplayName ?? installation.DefinitionKey}・{tool.Title ?? tool.Name}",
            // The server's own words, so data for the model: marked as such.
            $"（外部 connector「{installation.DisplayName ?? installation.DefinitionKey}」的工具；說明由該服務提供）{tool.Description}",
            tool.InputSchema,
            tool.Effect,
            installation.Id,
            installation.DisplayName ?? installation.DefinitionKey,
            PermissionKey: tool.Name);

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            try
            {
                var auth = await tokens.AuthOfAsync(installation, ct);
                var outcome = await mcp.CallToolAsync(new Uri(installation.ServerUrl!), auth, tool.Name, input, ct);
                return outcome.IsError
                    ? AiToolResult.Error(string.IsNullOrWhiteSpace(outcome.Text) ? "工具回報失敗。" : outcome.Text)
                    : new AiToolResult(outcome.Text);
            }
            catch (McpServerException ex)
            {
                logger.LogWarning(ex, "MCP tool {Tool} of connector {InstallationId} failed ({Failure})", tool.Name, installation.Id, ex.Failure);
                if (ex.Failure == McpFailure.NeedsAuth) await tokens.CredentialsRefusedAsync(installation.Id, ex.RequiredScope, CancellationToken.None);
                return AiToolResult.Error(ex.Failure != McpFailure.NeedsAuth ? $"「{_connectorName}」目前無法使用：{ex.Message}"
                    : ex.RequiredScope != null ? $"「{_connectorName}」的這個動作需要更多權限：請告訴使用者到 Connectors 頁面重新授權它，再試一次。"
                    : $"「{_connectorName}」需要重新連線：請告訴使用者到 Connectors 頁面更新它的認證。");
            }
        }
    }
}
