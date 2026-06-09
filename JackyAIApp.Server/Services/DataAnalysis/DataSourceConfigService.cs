using System.Text.Json;
using System.Text.RegularExpressions;
using DataAnalysisLibrary.Connectors.Adx;
using DataAnalysisLibrary.Connectors.MsSql;
using DataAnalysisLibrary.Core.Engine;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    public class DataSourceConfigService(ITokenEncryptionService encryptionService) : IDataSourceConfigService
    {
        private readonly ITokenEncryptionService _encryptionService = encryptionService;

        private const string MsSqlKey = "mssql";
        private const string AdxKey = "adx";

        public string SourceKey(DataSourceConnectorType type) =>
            type == DataSourceConnectorType.Adx ? AdxKey : MsSqlKey;

        public DataAnalysisEngine BuildEngine(DataSourceConfig source)
        {
            var decryptedConfigJson = _encryptionService.DecryptToken(source.ConfigJson);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return source.ConnectorType switch
            {
                DataSourceConnectorType.MsSql => BuildMsSqlEngine(decryptedConfigJson, opts),
                DataSourceConnectorType.Adx => BuildAdxEngine(decryptedConfigJson, opts),
                _ => throw new NotSupportedException($"Unsupported connector type: {source.ConnectorType}")
            };
        }

        private static DataAnalysisEngine BuildMsSqlEngine(string configJson, JsonSerializerOptions opts)
        {
            var cfg = JsonSerializer.Deserialize<MsSqlSourceConfigDto>(configJson, opts)
                      ?? throw new InvalidOperationException("Invalid MsSql config JSON.");
            return new DataAnalysisEngineBuilder()
                .AddSource(new MsSqlDataSourceFactory(new MsSqlConfig
                {
                    SourceKey = MsSqlKey,
                    ConnectionString = cfg.ConnectionString
                }))
                .Build();
        }

        private static DataAnalysisEngine BuildAdxEngine(string configJson, JsonSerializerOptions opts)
        {
            var cfg = JsonSerializer.Deserialize<AdxSourceConfigDto>(configJson, opts)
                      ?? throw new InvalidOperationException("Invalid Adx config JSON.");
            return new DataAnalysisEngineBuilder()
                .AddSource(new AdxDataSourceFactory(new AdxConfig
                {
                    SourceKey = AdxKey,
                    ClusterUri = cfg.ClusterUri,
                    Database = cfg.Database,
                    TenantId = cfg.TenantId,
                    ClientId = cfg.ClientId,
                    ClientSecret = cfg.ClientSecret,
                    ConnectionString = cfg.ConnectionString
                }))
                .Build();
        }

        public DataSourceConfigResponseDto ToResponseDto(DataSourceConfig source)
        {
            var decryptedJson = _encryptionService.DecryptToken(source.ConfigJson);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            object maskedConfig = source.ConnectorType switch
            {
                DataSourceConnectorType.MsSql => MaskMsSql(decryptedJson, opts),
                DataSourceConnectorType.Adx => MaskAdx(decryptedJson, opts),
                _ => new { }
            };
            return new DataSourceConfigResponseDto
            {
                Id = source.Id,
                Name = source.Name,
                ConnectorType = source.ConnectorType,
                Config = maskedConfig,
                DefaultTableName = source.DefaultTableName,
                CreatedAt = source.CreatedAt,
            };
        }

        private static object MaskMsSql(string json, JsonSerializerOptions opts)
        {
            var c = JsonSerializer.Deserialize<MsSqlSourceConfigDto>(json, opts);
            if (c is null) return new { };
            // Mask password in connection string but keep Server/Database visible.
            var masked = Regex.Replace(
                c.ConnectionString, @"(?i)(password|pwd)\s*=\s*[^;]+", "Password=***");
            return new MsSqlSourceConfigDto { ConnectionString = masked };
        }

        private static object MaskAdx(string json, JsonSerializerOptions opts)
        {
            var c = JsonSerializer.Deserialize<AdxSourceConfigDto>(json, opts);
            if (c is null) return new { };
            return new AdxSourceConfigDto
            {
                ClusterUri = c.ClusterUri,
                Database = c.Database,
                TenantId = c.TenantId,
                ClientId = c.ClientId,
                ClientSecret = string.IsNullOrEmpty(c.ClientSecret) ? null : "***",
                ConnectionString = string.IsNullOrEmpty(c.ConnectionString) ? null : "***",
            };
        }

        public string SerializeAndEncrypt(object config, DataSourceConnectorType type) =>
            _encryptionService.EncryptToken(SerializeConfig(config, type));

        public string MergeAndEncrypt(object newConfig, DataSourceConfig existing, DataSourceConnectorType type)
        {
            var newJson = SerializeConfig(newConfig, type);
            var decryptedExisting = _encryptionService.DecryptToken(existing.ConfigJson);
            var mergedJson = MergeConfigSecrets(newJson, decryptedExisting, type);
            return _encryptionService.EncryptToken(mergedJson);
        }

        private static string SerializeConfig(object config, DataSourceConnectorType type)
        {
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            // config may arrive as JsonElement (System.Text.Json model binding)
            if (config is JsonElement je)
            {
                return type switch
                {
                    DataSourceConnectorType.MsSql =>
                        JsonSerializer.Serialize(je.Deserialize<MsSqlSourceConfigDto>(opts), opts),
                    DataSourceConnectorType.Adx =>
                        JsonSerializer.Serialize(je.Deserialize<AdxSourceConfigDto>(opts), opts),
                    _ => je.GetRawText()
                };
            }
            return JsonSerializer.Serialize(config, opts);
        }

        /// <summary>
        /// Merges the incoming (potentially masked) config with the existing stored config.
        /// Non-secret fields are always taken from <paramref name="newJson"/>; secret fields
        /// ("***" placeholders) fall back to the values in <paramref name="existingJson"/>.
        /// </summary>
        private static string MergeConfigSecrets(string newJson, string existingJson, DataSourceConnectorType type)
        {
            var read = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var write = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            try
            {
                if (type == DataSourceConnectorType.Adx)
                {
                    var n = JsonSerializer.Deserialize<AdxSourceConfigDto>(newJson, read);
                    var o = JsonSerializer.Deserialize<AdxSourceConfigDto>(existingJson, read);
                    if (n is null) return existingJson;
                    return JsonSerializer.Serialize(new AdxSourceConfigDto
                    {
                        ClusterUri = n.ClusterUri,
                        Database = n.Database,
                        TenantId = n.TenantId,
                        ClientId = n.ClientId,
                        ClientSecret = n.ClientSecret == "***" ? o?.ClientSecret : n.ClientSecret,
                        ConnectionString = n.ConnectionString == "***" ? o?.ConnectionString : n.ConnectionString,
                    }, write);
                }
                if (type == DataSourceConnectorType.MsSql)
                {
                    var n = JsonSerializer.Deserialize<MsSqlSourceConfigDto>(newJson, read);
                    var o = JsonSerializer.Deserialize<MsSqlSourceConfigDto>(existingJson, read);
                    if (n is null) return existingJson;
                    var connStr = n.ConnectionString.Contains("Password=***")
                        ? o?.ConnectionString ?? n.ConnectionString
                        : n.ConnectionString;
                    return JsonSerializer.Serialize(new MsSqlSourceConfigDto { ConnectionString = connStr }, write);
                }
            }
            catch { /* fall through */ }
            return newJson;
        }
    }
}
