using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// The connectors page's AI part (docs/ai-native/design/03 §7): what the reader may add, the
    /// connectors they have — the site's own tools always among them — and what the AI may do with
    /// each of their tools.
    /// </summary>
    public interface IConnectorCatalogService
    {
        Task<ServiceResult<ConnectorCatalogDto>> GetAsync(CancellationToken ct);

        /// <summary>
        /// Sets what the AI may do with one tool. An always-on connector (the site's own tools) has no
        /// row until the first change, which makes it. Unknown tools and permissions are a bad request; a
        /// tool that cannot be undone cannot be set to run on its own.
        /// </summary>
        Task<ServiceResult<InstalledConnectorDto>> SetToolPermissionAsync(ToolPermissionRequestDto request, CancellationToken ct);

        /// <summary>Turns one of the reader's connectors off (its tools are not offered) or on again.</summary>
        Task<ServiceResult<InstalledConnectorDto>> SetEnabledAsync(string installationId, bool enabled, CancellationToken ct);

        /// <summary>Removes one of the reader's connectors, with its credentials.</summary>
        Task<ServiceResult<NoData>> DeleteAsync(string installationId, CancellationToken ct);
    }

    /// <summary>What the reader chose for a connector's tools, for a turn of the chat.</summary>
    public interface IToolPermissionSource
    {
        /// <summary>
        /// The reader's own choices for the tools of a connector (none = the defaults): <paramref name="connectorKey"/>
        /// is <see cref="SiteConnector.Key"/> for the site's own, else the installation's id.
        /// </summary>
        Task<IReadOnlyDictionary<string, ToolPermission>> ChosenAsync(string userId, string connectorKey, CancellationToken ct);
    }

    public class ConnectorCatalogService(
        AzureSQLDBContext db, ICurrentUser currentUser, IEnumerable<SiteToolSet> siteTools, TimeProvider time)
        : IConnectorCatalogService, IToolPermissionSource
    {
        /// <summary>How many MCP servers of their own one reader may add.</summary>
        public const int CustomLimit = 20;

        private readonly IReadOnlyList<AiToolDescriptor> _siteTools = siteTools.SelectMany(s => s.Tools).ToList();

        private static ServiceFailure NotFound() => ServiceResult.Fail(ErrorCodes.NotFound, "找不到這個 connector。");

        public async Task<ServiceResult<ConnectorCatalogDto>> GetAsync(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var rows = await db.ConnectorInstallations.AsNoTracking()
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(ct);

            var siteRow = rows.FirstOrDefault(r => r.DefinitionKey == SiteConnector.Key);
            var installed = new List<InstalledConnectorDto> { SiteDto(siteRow) };
            installed.AddRange(rows.Where(r => r.DefinitionKey != SiteConnector.Key).Select(InstalledDto));

            // The site's own tools are always among the installed, so the catalog lists only what can be added.
            var entries = BuiltInMcpConnectors.All
                .Select(d => new CatalogEntryDto(d.Key, d.DisplayName, d.Description, d.Category, d.Publisher, d.AuthKind,
                    d.DocsUrl, Installed: rows.Any(r => r.DefinitionKey == d.Key)))
                .ToList();
            return ServiceResult.Ok(new ConnectorCatalogDto(entries, installed, CustomLimit));
        }

        public async Task<IReadOnlyDictionary<string, ToolPermission>> ChosenAsync(string userId, string connectorKey, CancellationToken ct)
        {
            // The site's own tools go by its key (one row at most); any other connector's by its installation's id.
            var json = await db.ConnectorInstallations.AsNoTracking()
                .Where(c => c.UserId == userId &&
                            (connectorKey == SiteConnector.Key ? c.DefinitionKey == SiteConnector.Key : c.Id == connectorKey))
                .Select(c => c.ToolSettingsJson)
                .FirstOrDefaultAsync(ct);
            return ToolPermissions.Parse(json);
        }

        public async Task<ServiceResult<InstalledConnectorDto>> SetToolPermissionAsync(ToolPermissionRequestDto request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (ToolPermissions.FromWireName(request.Permission) is not { } permission)
                return ServiceResult.Fail(ErrorCodes.BadRequest, "不認得的權限。");

            SQL.ConnectorInstallation? row;
            IReadOnlyList<(string Name, ToolEffect Effect, bool AlwaysAsks)> tools;
            if (request.InstallationId == null)
            {
                // Only an always-on connector is set without a row; the site's own is the one there is.
                if (request.DefinitionKey != SiteConnector.Key) return NotFound();
                row = await db.ConnectorInstallations
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.DefinitionKey == SiteConnector.Key, ct);
                tools = _siteTools.Select(t => (t.Name, t.Effect, t.AlwaysAsks)).ToList();
            }
            else
            {
                row = await db.ConnectorInstallations
                    .FirstOrDefaultAsync(c => c.Id == request.InstallationId && c.UserId == userId, ct);
                if (row == null || row.DefinitionKey != request.DefinitionKey) return NotFound();
                tools = row.DefinitionKey == SiteConnector.Key
                    ? _siteTools.Select(t => (t.Name, t.Effect, t.AlwaysAsks)).ToList()
                    : CachedTools.Parse(row.ToolsCacheJson).Select(t => (t.Name, t.Effect, false)).ToList();
            }

            var tool = tools.FirstOrDefault(t => t.Name == request.ToolName);
            if (tool.Name == null) return ServiceResult.Fail(ErrorCodes.BadRequest, "這個 connector 沒有這個工具。");
            if (!ToolPermissions.Allowed(tool.Effect, permission))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "無法復原的動作一定要先問你，不能設成自動執行。");
            if (tool.AlwaysAsks && permission == ToolPermission.Auto)
                return ServiceResult.Fail(ErrorCodes.BadRequest, "這個動作會影響之後的每個對話，一定要先問你，不能設成自動執行。");

            var now = time.GetUtcNow().UtcDateTime;
            if (row == null)
            {
                row = new SQL.ConnectorInstallation
                {
                    UserId = userId,
                    DefinitionKey = SiteConnector.Key,
                    AuthKind = "None",
                    LastStatus = "Ok",
                    CreatedAt = now,
                };
                db.ConnectorInstallations.Add(row);
            }
            var chosen = ToolPermissions.Parse(row.ToolSettingsJson);
            chosen[tool.Name] = permission;
            row.ToolSettingsJson = ToolPermissions.Serialize(chosen);
            row.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            return ServiceResult.Ok(row.DefinitionKey == SiteConnector.Key ? SiteDto(row) : InstalledDto(row));
        }

        public async Task<ServiceResult<InstalledConnectorDto>> SetEnabledAsync(string installationId, bool enabled, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var row = await db.ConnectorInstallations.FirstOrDefaultAsync(c => c.Id == installationId && c.UserId == userId, ct);
            // The site's own tools are always there; a tool of theirs is turned off one by one.
            if (row == null || row.DefinitionKey == SiteConnector.Key) return NotFound();

            row.Enabled = enabled;
            row.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return ServiceResult.Ok(InstalledDto(row));
        }

        public async Task<ServiceResult<NoData>> DeleteAsync(string installationId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var row = await db.ConnectorInstallations.FirstOrDefaultAsync(c => c.Id == installationId && c.UserId == userId, ct);
            if (row == null || row.DefinitionKey == SiteConnector.Key) return NotFound();

            db.ConnectorInstallations.Remove(row);
            await db.SaveChangesAsync(ct);
            return ServiceResult.Ok();
        }

        private InstalledConnectorDto SiteDto(SQL.ConnectorInstallation? row)
        {
            var chosen = ToolPermissions.Parse(row?.ToolSettingsJson);
            var tools = _siteTools
                .Select(t => new ConnectorToolDto(t.Name, t.DisplayName, t.Description, t.Effect.WireName(),
                    ToolPermissions.For(t, chosen).WireName()))
                .ToList();
            return new InstalledConnectorDto(row?.Id, SiteConnector.Key, SiteConnector.DisplayName, SiteConnector.Description,
                "site", null, "none", "ok", AlwaysOn: true, null, null, [], tools);
        }

        internal static InstalledConnectorDto InstalledDto(SQL.ConnectorInstallation row)
        {
            var chosen = ToolPermissions.Parse(row.ToolSettingsJson);
            var tools = CachedTools.Parse(row.ToolsCacheJson)
                .Select(t => new ConnectorToolDto(t.Name, t.Title, t.Description, t.Effect.WireName(),
                    ToolPermissions.For(t.Name, t.Effect, chosen).WireName()))
                .ToList();
            var builtIn = BuiltInMcpConnectors.Find(row.DefinitionKey);
            return new InstalledConnectorDto(row.Id, row.DefinitionKey, row.DisplayName ?? builtIn?.DisplayName ?? row.DefinitionKey,
                builtIn?.Description ?? (row.ServerUrl == null ? "" : $"MCP server：{new Uri(row.ServerUrl).Host}"),
                builtIn?.Category ?? "data",
                row.ServerUrl, AuthKindWireName(row.AuthKind), HealthOf(row), AlwaysOn: false, row.LastError, row.LastTestedAt,
                [], tools);
        }

        internal static string HealthOf(SQL.ConnectorInstallation row) =>
            !row.Enabled ? "disabled" : row.LastStatus switch
            {
                "Ok" => "ok",
                "NeedsAuth" => "needsAuth",
                "Error" => "error",
                _ => "unknown",
            };

        internal static string AuthKindWireName(string authKind) => authKind switch
        {
            "ApiKey" => "apiKey",
            "Bearer" => "bearer",
            "OAuth" => "oauth",
            _ => "none",
        };
    }
}
