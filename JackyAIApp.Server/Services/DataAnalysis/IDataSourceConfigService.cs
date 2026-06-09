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
        /// <summary>Builds a query engine for the given (encrypted) source config.</summary>
        DataAnalysisEngine BuildEngine(DataSourceConfig source);

        /// <summary>The DAL source key for a connector type (matches the key BuildEngine uses).</summary>
        string SourceKey(DataSourceConnectorType type);

        /// <summary>Maps a stored config to a response DTO with secrets masked.</summary>
        DataSourceConfigResponseDto ToResponseDto(DataSourceConfig source);

        /// <summary>Serializes + encrypts a config for a brand-new source.</summary>
        string SerializeAndEncrypt(object config, DataSourceConnectorType type);

        /// <summary>
        /// Serializes the incoming config, merges masked secrets with the existing stored
        /// config, and returns the encrypted result (for updates).
        /// </summary>
        string MergeAndEncrypt(object newConfig, DataSourceConfig existing, DataSourceConnectorType type);
    }
}
