using DataAnalysisLibrary.Core.Engine;
using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// A resolved query target: the engine plus the source key and table to run against. When
    /// <see cref="OwnsEngine"/> is true the engine was built on the fly for a custom source and must
    /// be disposed after use; the singleton default engine (OwnsEngine = false) must not be.
    /// </summary>
    public sealed record ResolvedSource(
        DataAnalysisEngine Engine,
        string SourceKey,
        string TableName,
        bool OwnsEngine) : IDisposable
    {
        public void Dispose() { if (OwnsEngine) Engine.Dispose(); }
    }

    public enum DataSourceResolution
    {
        Ok,
        /// <summary>No source id, or it isn't the owner's; the caller maps this to its own 404.</summary>
        NotFound,
        /// <summary>The saved source's settings were refused (see <see cref="DataSourceConfigException"/>); 400 + message.</summary>
        ConfigRefused,
    }

    /// <summary>
    /// Resolves a DataAnalysis query to its engine, shared by the DataAnalysis demo controller and
    /// saved dashboard widgets so both go through one hardened path: no source id is the app's own
    /// database pinned to the demo table; a source id must be the named owner's, and its saved
    /// settings are validated once here. Deliberately HTTP-agnostic — callers map the status to
    /// their own response (the 404 text and the engine-error visibility rules differ between them).
    /// </summary>
    public interface IDataSourceResolver
    {
        /// <summary>The default source's key and table — the app's own database, demo table only.</summary>
        string DefaultSourceKey { get; }
        string DefaultTableName { get; }

        /// <summary>
        /// Resolves <paramref name="sourceId"/> for <paramref name="ownerUserId"/> (the record's owner
        /// — the signed-in caller for a live query, the dashboard owner for a widget). A null source id
        /// gives the default source and never honours <paramref name="tableName"/>, so the default
        /// source can't be aimed at an arbitrary app table.
        /// </summary>
        Task<(ResolvedSource? Source, DataSourceResolution Status, string? Message)> ResolveAsync(
            Guid? sourceId, string? tableName, string? ownerUserId, CancellationToken ct);
    }

    public class DataSourceResolver(
        DataAnalysisEngine defaultEngine,
        AzureSQLDBContext dbContext,
        IDataSourceConfigService configService,
        IServerAddressCheck addressCheck,
        ILogger<DataSourceResolver> logger) : IDataSourceResolver
    {
        /// <summary>What the owner is told when their source's server resolves to an address that is not on the internet (D-164).</summary>
        internal const string PrivateServerMessage =
            "The server's name does not resolve to an address on the internet; a custom source must be a SQL Server reachable over the internet.";

        private const string DefaultMsSqlKey  = "mssql";
        private const string DefaultTable     = "dbo.Orders";

        public string DefaultSourceKey => DefaultMsSqlKey;
        public string DefaultTableName => DefaultTable;

        public async Task<(ResolvedSource? Source, DataSourceResolution Status, string? Message)> ResolveAsync(
            Guid? sourceId, string? tableName, string? ownerUserId, CancellationToken ct)
        {
            if (sourceId is null)
                // The default source is the application's own database, pinned to the demo table so it
                // can't be used to read arbitrary app tables.
                return (new ResolvedSource(defaultEngine, DefaultMsSqlKey, DefaultTable, OwnsEngine: false),
                        DataSourceResolution.Ok, null);

            // Custom sources require a known owner who holds the record.
            if (ownerUserId is null)
                return (null, DataSourceResolution.NotFound, null);

            var source = await dbContext.DataSourceConfigs
                .FirstOrDefaultAsync(s => s.Id == sourceId.Value && s.UserId == ownerUserId, ct);
            if (source is null)
                return (null, DataSourceResolution.NotFound, null);

            // The connection string is checked as written when the engine is built; its server's name could still
            // point inside (D-164). Checked first, so nothing is left to dispose if the check stops or throws.
            foreach (var host in configService.ServerHostsOf(source))
            {
                if (await addressCheck.IsPublicAsync(host, ct)) continue;
                logger.LogWarning("DataAnalysis: refused saved source {id}: its server does not resolve to a public address", source.Id);
                return (null, DataSourceResolution.ConfigRefused, PrivateServerMessage);
            }

            DataAnalysisEngine engine;
            try
            {
                engine = configService.BuildEngine(source);
            }
            catch (DataSourceConfigException ex)
            {
                logger.LogWarning("DataAnalysis: refused saved source {id}: {err}", source.Id, ex.Message);
                return (null, DataSourceResolution.ConfigRefused, ex.Message);
            }

            var sourceKey = configService.SourceKey(source.ConnectorType);
            var resolvedTable = tableName ?? source.DefaultTableName ?? "";
            return (new ResolvedSource(engine, sourceKey, resolvedTable, OwnsEngine: true), DataSourceResolution.Ok, null);
        }
    }
}
