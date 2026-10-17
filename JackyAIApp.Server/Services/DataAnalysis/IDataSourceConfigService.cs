using DataAnalysisLibrary.Core.Engine;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.DataAnalysis;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Encapsulates everything about turning a saved <see cref="DataSourceConfig"/>
    /// into a query engine, a masked response DTO, or an encrypted config blob —
    /// including connector-specific (de)serialization and secret masking/merging.
    /// </summary>
    public interface IDataSourceConfigService
    {
        /// <summary>
        /// Builds a query engine for the given (encrypted) source config. Throws
        /// <see cref="DataSourceConfigException"/> when the stored config is refused by
        /// <see cref="DataSourceConfigValidator"/> — it never gets a connection.
        /// </summary>
        DataAnalysisEngine BuildEngine(DataSourceConfig source);

        /// <summary>The DAL source key for a connector type (matches the key BuildEngine uses).</summary>
        string SourceKey(DataSourceConnectorType type);

        /// <summary>Maps a stored config to a response DTO with secrets masked.</summary>
        DataSourceConfigResponseDto ToResponseDto(DataSourceConfig source);

        /// <summary>
        /// Serializes + validates + encrypts a config for a brand-new source. Throws
        /// <see cref="DataSourceConfigException"/> when it is malformed or refused.
        /// </summary>
        string SerializeAndEncrypt(object config, DataSourceConnectorType type);

        /// <summary>
        /// Serializes the incoming config, merges masked secrets with the existing stored
        /// config, validates the merged result and returns it encrypted (for updates). Throws
        /// <see cref="DataSourceConfigException"/> when it is malformed or refused.
        /// </summary>
        string MergeAndEncrypt(object newConfig, DataSourceConfig existing, DataSourceConnectorType type);
    }
}
