using System.Text.Json;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.DataAnalysis;
using Moq;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// A custom source has to authenticate with its owner's own credentials. Anything that would let the
    /// driver use an identity of the server itself — managed identity, the default credential chain,
    /// Windows auth — is refused on save and again before a stored config gets a connection.
    /// </summary>
    public class DataSourceConfigValidatorTests
    {
        private const string Server = "Server=tcp:demo.database.windows.net,1433;Database=Sales;";

        private static MsSqlSourceConfigDto Sql(string connectionString) => new() { ConnectionString = connectionString };

        [Theory]
        [InlineData("Authentication=Active Directory Managed Identity;User ID=x;Password=y;")]
        [InlineData("Authentication=ActiveDirectoryManagedIdentity;")]
        [InlineData("Authentication=Active Directory MSI;")]
        [InlineData("Authentication=Active Directory Default;")]
        [InlineData("Authentication=Active Directory Workload Identity;")]
        [InlineData("Authentication=Active Directory Integrated;")]
        [InlineData("Authentication=Active Directory Interactive;User ID=me@example.com;")]
        [InlineData("Authentication=Active Directory Device Code Flow;")]
        [InlineData("Integrated Security=true;")]
        [InlineData("Integrated Security=SSPI;User ID=u;Password=p;")]
        [InlineData("User ID=u;")]                                         // no password
        [InlineData("Password=p;")]                                        // no user
        [InlineData("User ID=u;Password= ;")]
        [InlineData("User ID=u;Password=p;AttachDBFilename=C:\\data\\x.mdf;")]
        [InlineData("User ID=u;Password=p;Connect Timeout=0;")]
        [InlineData("User ID=u;Password=p;Connect Timeout=600;")]
        public void MsSql_ForbiddenOrIncompleteAuthentication_IsRefused(string tail) =>
            Assert.NotNull(DataSourceConfigValidator.Validate(Sql(Server + tail)));

        [Theory]
        [InlineData("not a connection string")]
        [InlineData("Server=x;Bogus Keyword=1;User ID=u;Password=p;")]
        [InlineData("")]
        public void MsSql_UnparseableConnectionString_IsRefused(string connectionString) =>
            Assert.NotNull(DataSourceConfigValidator.Validate(Sql(connectionString)));

        [Theory]
        [InlineData("User ID=sales_reader;Password=s3cret!;")]
        [InlineData("Authentication=SqlPassword;User ID=sales_reader;Password=s3cret!;Connect Timeout=30;")]
        [InlineData("Authentication=Active Directory Password;User ID=me@contoso.com;Password=s3cret!;")]
        [InlineData("Authentication=Active Directory Service Principal;User ID=11111111-2222-3333-4444-555555555555;Password=client-secret;")]
        public void MsSql_OwnersOwnCredentials_AreAccepted(string tail) =>
            Assert.Null(DataSourceConfigValidator.Validate(Sql(Server + tail)));

        private static AdxSourceConfigDto Adx(string clusterUri = "https://mycluster.westus.kusto.windows.net",
            string? connectionString = null, string? clientSecret = "secret") => new()
        {
            ClusterUri = clusterUri,
            Database = "Telemetry",
            TenantId = "tenant",
            ClientId = "client",
            ClientSecret = clientSecret,
            ConnectionString = connectionString,
        };

        [Theory]
        [InlineData("https://mycluster.westus.kusto.windows.net")]
        [InlineData("https://help.kusto.windows.net/")]
        [InlineData("https://mycluster.kusto.data.microsoft.com")]
        [InlineData("https://trd-abc123.z0.kusto.fabric.microsoft.com")]
        [InlineData("https://mycluster.kustomfa.windows.net")]
        public void Adx_ServicePrincipalOnAKustoCluster_IsAccepted(string clusterUri) =>
            Assert.Null(DataSourceConfigValidator.Validate(Adx(clusterUri)));

        [Fact]
        public void Adx_RawConnectionString_IsRefused() =>
            Assert.NotNull(DataSourceConfigValidator.Validate(
                Adx(connectionString: "Data Source=https://mycluster.kusto.windows.net;Fed=true;ManagedServiceIdentity=system")));

        [Fact]
        public void Adx_MissingClientSecret_IsRefused() =>
            Assert.NotNull(DataSourceConfigValidator.Validate(Adx(clientSecret: null)));

        [Theory]
        [InlineData("https://evil.example.com")]
        [InlineData("http://mycluster.kusto.windows.net")]                       // not https
        [InlineData("https://kusto.windows.net")]                                // no cluster
        [InlineData("https://mycluster.kusto.windows.net.evil.example")]
        [InlineData("https://169.254.169.254")]
        [InlineData("https://user@mycluster.kusto.windows.net")]
        [InlineData("https://mycluster.kusto.windows.net?x=1")]
        [InlineData("https://mycluster.kusto.windows.net;Fed=true;Data Source=https://evil.example.com")]
        [InlineData("mycluster.kusto.windows.net")]
        [InlineData("")]
        public void Adx_ClusterUriOutsideKusto_IsRefused(string clusterUri) =>
            Assert.NotNull(DataSourceConfigValidator.Validate(Adx(clusterUri)));

        // ── Through the service: save, update, and a stored config ──────────

        private static DataSourceConfigService Service()
        {
            var crypto = new Mock<ITokenEncryptionService>();
            crypto.Setup(c => c.EncryptToken(It.IsAny<string>())).Returns((string s) => s);
            crypto.Setup(c => c.DecryptToken(It.IsAny<string>())).Returns((string s) => s);
            return new DataSourceConfigService(crypto.Object);
        }

        private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

        private static DataSourceConfig Stored(DataSourceConnectorType type, object config) => new()
        {
            Id = Guid.NewGuid(),
            UserId = "owner",
            Name = "src",
            ConnectorType = type,
            ConfigJson = JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };

        [Fact]
        public void Save_ManagedIdentityConnectionString_Throws()
        {
            var ex = Assert.Throws<DataSourceConfigException>(() => Service().SerializeAndEncrypt(
                Json(new { connectionString = Server + "Authentication=Active Directory Managed Identity;" }),
                DataSourceConnectorType.MsSql));
            Assert.Contains("not allowed", ex.Message);
        }

        [Fact]
        public void Save_MissingRequiredField_ThrowsAConfigError() =>
            Assert.Throws<DataSourceConfigException>(() =>
                Service().SerializeAndEncrypt(Json(new { database = "x" }), DataSourceConnectorType.Adx));

        [Fact]
        public void Update_MaskedPassword_IsValidatedAfterMergingTheStoredOne()
        {
            var existing = Stored(DataSourceConnectorType.MsSql, new { connectionString = Server + "User ID=u;Password=real;" });

            var merged = Service().MergeAndEncrypt(
                Json(new { connectionString = Server + "User ID=u;Password=***;" }), existing, DataSourceConnectorType.MsSql);

            Assert.Contains("Password=real", merged);
        }

        [Fact]
        public void Update_SwitchingToIntegratedSecurity_Throws()
        {
            var existing = Stored(DataSourceConnectorType.MsSql, new { connectionString = Server + "User ID=u;Password=real;" });

            Assert.Throws<DataSourceConfigException>(() => Service().MergeAndEncrypt(
                Json(new { connectionString = Server + "Integrated Security=true;" }), existing, DataSourceConnectorType.MsSql));
        }

        [Fact]
        public void Update_KeepingAStoredRawAdxConnectionString_Throws()
        {
            var existing = Stored(DataSourceConnectorType.Adx, new
            {
                clusterUri = "https://mycluster.kusto.windows.net", database = "db",
                tenantId = "t", clientId = "c", clientSecret = "s",
                connectionString = "Data Source=https://mycluster.kusto.windows.net;Fed=true",
            });

            // The masked "***" placeholder would carry the stored connection string over.
            Assert.Throws<DataSourceConfigException>(() => Service().MergeAndEncrypt(
                Json(new
                {
                    clusterUri = "https://mycluster.kusto.windows.net", database = "db",
                    tenantId = "t", clientId = "c", clientSecret = "***", connectionString = "***",
                }), existing, DataSourceConnectorType.Adx));
        }

        [Fact]
        public void BuildEngine_StoredConfigThatNoLongerPasses_ThrowsInsteadOfConnecting()
        {
            var stored = Stored(DataSourceConnectorType.MsSql, new { connectionString = Server + "Authentication=Active Directory Default;" });

            Assert.Throws<DataSourceConfigException>(() => Service().BuildEngine(stored));
        }

        [Fact]
        public void BuildEngine_StoredRawAdxConnectionString_ThrowsInsteadOfConnecting()
        {
            var stored = Stored(DataSourceConnectorType.Adx, new
            {
                clusterUri = "https://mycluster.kusto.windows.net", database = "db",
                tenantId = "t", clientId = "c", clientSecret = "s",
                connectionString = "Data Source=https://mycluster.kusto.windows.net;Fed=true;AzCli=true",
            });

            Assert.Throws<DataSourceConfigException>(() => Service().BuildEngine(stored));
        }

        [Fact]
        public void BuildEngine_ValidSqlLogin_BuildsAnEngine()
        {
            var stored = Stored(DataSourceConnectorType.MsSql, new { connectionString = Server + "User ID=u;Password=p;" });

            using var engine = Service().BuildEngine(stored);

            Assert.NotNull(engine);
        }
    }
}
