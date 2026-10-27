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
            // A stored config is checked again before it gets a connection: one saved before the
            // validator existed, or under older rules, is refused here with the reason rather than used.
            var decryptedConfigJson = _encryptionService.DecryptToken(source.ConfigJson);
            return source.ConnectorType switch
            {
                DataSourceConnectorType.MsSql => BuildMsSqlEngine(
                    ReadValidated<MsSqlSourceConfigDto>(decryptedConfigJson, DataSourceConfigValidator.Validate)),
                DataSourceConnectorType.Adx => BuildAdxEngine(
                    ReadValidated<AdxSourceConfigDto>(decryptedConfigJson, DataSourceConfigValidator.Validate)),
                _ => throw new DataSourceConfigException($"Unsupported connector type: {source.ConnectorType}.")
            };
        }

        public string[] ServerHostsOf(DataSourceConfig source)
        {
            if (source.ConnectorType != DataSourceConnectorType.MsSql) return [];
            try
            {
                var cfg = JsonSerializer.Deserialize<MsSqlSourceConfigDto>(_encryptionService.DecryptToken(source.ConfigJson), ReadOptions);
                return cfg is null ? [] : DataSourceConfigValidator.ServerHostsOf(cfg);
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Reads a config and runs it through <see cref="DataSourceConfigValidator"/>; throws
        /// <see cref="DataSourceConfigException"/> when it can't be read or is refused.
        /// </summary>
        private static T ReadValidated<T>(string configJson, Func<T?, string?> validate) where T : class
        {
            T? cfg;
            try
            {
                cfg = JsonSerializer.Deserialize<T>(configJson, ReadOptions);
            }
            catch (JsonException)
            {
                throw new DataSourceConfigException("The data source settings could not be read.");
            }
            if (validate(cfg) is { } error) throw new DataSourceConfigException(error);
            return cfg!;
        }

        private static DataAnalysisEngine BuildMsSqlEngine(MsSqlSourceConfigDto cfg)
        {
            return new DataAnalysisEngineBuilder()
                .AddSource(new MsSqlDataSourceFactory(new MsSqlConfig
                {
                    SourceKey = MsSqlKey,
                    ConnectionString = cfg.ConnectionString
                }))
                .Build();
        }

        private static DataAnalysisEngine BuildAdxEngine(AdxSourceConfigDto cfg)
        {
            return new DataAnalysisEngineBuilder()
                .AddSource(new AdxDataSourceFactory(new AdxConfig
                {
                    SourceKey = AdxKey,
                    ClusterUri = cfg.ClusterUri,
                    Database = cfg.Database,
                    TenantId = cfg.TenantId,
                    ClientId = cfg.ClientId,
                    ClientSecret = cfg.ClientSecret,
                    // Never the raw connection string: the validator refuses one, and the service-principal
                    // fields above are the only authentication a custom ADX source may use.
                    ConnectionString = null,
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

        public string SerializeAndEncrypt(object config, DataSourceConnectorType type)
        {
            var json = SerializeConfig(config, type);
            EnsureValid(json, type);
            return _encryptionService.EncryptToken(json);
        }

        public string MergeAndEncrypt(object newConfig, DataSourceConfig existing, DataSourceConnectorType type)
        {
            var newJson = SerializeConfig(newConfig, type);
            var decryptedExisting = _encryptionService.DecryptToken(existing.ConfigJson);
            var mergedJson = MergeConfigSecrets(newJson, decryptedExisting, type);
            // What gets stored is the merge (masked secrets filled in from the old config), so that is what is checked.
            EnsureValid(mergedJson, type);
            return _encryptionService.EncryptToken(mergedJson);
        }

        private static void EnsureValid(string configJson, DataSourceConnectorType type)
        {
            switch (type)
            {
                case DataSourceConnectorType.MsSql:
                    ReadValidated<MsSqlSourceConfigDto>(configJson, DataSourceConfigValidator.Validate);
                    break;
                case DataSourceConnectorType.Adx:
                    ReadValidated<AdxSourceConfigDto>(configJson, DataSourceConfigValidator.Validate);
                    break;
                default:
                    throw new DataSourceConfigException($"Unsupported connector type: {type}.");
            }
        }

        private static string SerializeConfig(object config, DataSourceConnectorType type)
        {
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            try
            {
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
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new DataSourceConfigException("The data source settings are missing a required field or are malformed.");
            }
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
