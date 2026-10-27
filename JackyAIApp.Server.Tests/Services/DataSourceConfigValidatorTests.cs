using System.Net;
using System.Net.Sockets;
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

        private const string Login = "User ID=u;Password=p;";

        [Theory]
        [InlineData("Server=10.0.0.4,1433;")]                          // private network
        [InlineData("Server=tcp:169.254.169.254,80;")]                 // cloud metadata
        [InlineData("Server=127.0.0.1;")]
        [InlineData("Server=tcp:[::1],1433;")]
        [InlineData("Server=0x7f000001;")]                              // loopback, written as one number
        [InlineData("Server=2130706433;")]
        [InlineData("Server=192.168.1.10\\SQLEXPRESS;")]
        [InlineData("Server=localhost,8080;")]
        [InlineData("Server=db.localhost;")]
        [InlineData("Server=.;")]
        [InlineData("Server=(local);")]
        [InlineData("Server=(localdb)\\MSSQLLocalDB;")]
        [InlineData("Server=fileserver;")]                             // a bare name: a machine on the local network
        [InlineData("Server=np:\\\\host\\pipe\\sql\\query;")]           // named pipes
        [InlineData("Server=lpc:demo.database.windows.net;")]           // shared memory
        [InlineData("Server=admin:demo.database.windows.net;")]
        [InlineData("Server=demo.database.windows.net,99999;")]
        [InlineData("Server=demo.database.windows.net,abc;")]
        [InlineData("Server=demo.database.windows.net;Failover Partner=10.1.2.3;")]
        [InlineData("Server=demo.database.windows.net;Enclave Attestation Url=http://169.254.169.254/metadata;")]
        [InlineData("Database=Sales;")]                                // no server
        public void MsSql_AServerThatIsNotOnTheInternet_IsRefused(string server)
        {
            var error = DataSourceConfigValidator.Validate(Sql(server + Login));

            Assert.NotNull(error);
        }

        [Theory]
        [InlineData("Server=tcp:demo.database.windows.net,1433;")]
        [InlineData("Server=demo.database.windows.net;")]
        [InlineData("Data Source=sql.example.com,14330;")]
        [InlineData("Server=sql.example.com\\REPORTING;")]
        [InlineData("Server=203.0.114.20,1433;")]                       // a public address
        [InlineData("Server=reports_db.example.com;")]                  // '_' is in some real DNS names
        [InlineData("Server=tcp:mi-1.public.abc123.database.windows.net,3342;")] // a managed instance's public endpoint
        [InlineData("Server=demo.database.windows.net;Failover Partner=demo-dr.database.windows.net;")]
        public void MsSql_AServerOnTheInternet_IsAccepted(string server) =>
            Assert.Null(DataSourceConfigValidator.Validate(Sql(server + Login)));

        [Theory]
        [InlineData("Server=tcp:demo.database.windows.net,1433;", new[] { "demo.database.windows.net" })]
        [InlineData("Server=sql.example.com\\REPORTING;Failover Partner=tcp:dr.example.com,1433;", new[] { "sql.example.com", "dr.example.com" })]
        [InlineData("Server=tcp:[2001:4860::8888],1433;", new[] { "2001:4860::8888" })]
        public void MsSql_TheHostsItConnectsTo_AreItsServerAndFailoverPartner(string server, string[] hosts) =>
            Assert.Equal(hosts, DataSourceConfigValidator.ServerHostsOf(Sql(server + Login)));

        [Fact]
        public void TheService_ReadsAStoredSqlSourcesHosts_AndNoneForAnyOther()
        {
            var sql = Stored(DataSourceConnectorType.MsSql, new { connectionString = Server + Login });
            var adx = Stored(DataSourceConnectorType.Adx, new { clusterUri = "https://mycluster.kusto.windows.net", database = "db" });

            Assert.Equal(new[] { "demo.database.windows.net" }, Service().ServerHostsOf(sql));
            Assert.Empty(Service().ServerHostsOf(adx));
            Assert.Empty(DataSourceConfigValidator.ServerHostsOf(Sql("not a connection string")));
        }

        [Fact]
        public async Task TheDnsCheck_TakesANameWhoseEveryAddressIsPublic_AndNothingElse()
        {
            var answers = new Dictionary<string, IPAddress[]>
            {
                ["public.example.com"] = [IPAddress.Parse("20.1.2.3"), IPAddress.Parse("2603:1030::1")],
                ["mixed.example.com"] = [IPAddress.Parse("20.1.2.3"), IPAddress.Parse("10.0.0.4")],
                ["metadata.example.com"] = [IPAddress.Parse("169.254.169.254")],
                ["empty.example.com"] = [],
            };
            var check = new DnsServerAddressCheck((host, _) => answers.TryGetValue(host, out var a)
                ? Task.FromResult(a)
                : throw new SocketException((int)SocketError.HostNotFound));

            Assert.True(await check.IsPublicAsync("public.example.com", CancellationToken.None));
            Assert.False(await check.IsPublicAsync("mixed.example.com", CancellationToken.None));
            Assert.False(await check.IsPublicAsync("metadata.example.com", CancellationToken.None));
            Assert.False(await check.IsPublicAsync("empty.example.com", CancellationToken.None));
            Assert.False(await check.IsPublicAsync("nowhere.example.com", CancellationToken.None));
            Assert.False(await check.IsPublicAsync("127.0.0.1", CancellationToken.None));
            Assert.True(await check.IsPublicAsync("20.1.2.3", CancellationToken.None));
        }

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
        public void Update_TheMaskedStringAsShown_KeepsTheStoredPassword()
        {
            var existing = Stored(DataSourceConnectorType.MsSql, new { connectionString = Server + "User ID=u;Password=\"re;al\";" });
            var shown = DataSourceConfigService.MaskPassword(Server + "User ID=u;Password=\"re;al\";");

            var merged = Service().MergeAndEncrypt(Json(new { connectionString = shown }), existing, DataSourceConnectorType.MsSql);

            Assert.Contains("re;al", merged);
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

        [Theory]
        [InlineData("Server=tcp:db.example.com,1433;Database=d;User ID=u;Password=real;")]
        [InlineData("Server=tcp:db.example.com,1433;Database=d;User ID=u;Password=\"se;cret\";")]
        [InlineData("Server=tcp:db.example.com,1433;Database=d;User ID=u;Pwd='a;b;c'")]
        public void AShownConnectionString_HidesTheWholePassword_AndKeepsTheServer(string connectionString)
        {
            // A quoted password holding ";" was masked only up to it, showing the rest (D-165).
            var masked = DataSourceConfigService.MaskPassword(connectionString);

            Assert.Contains("Password=***", masked);
            Assert.Contains("db.example.com", masked);
            Assert.DoesNotContain("real", masked);
            Assert.DoesNotContain("cret", masked);
            Assert.DoesNotContain("b;c", masked);
        }
    }
}
