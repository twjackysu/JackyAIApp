using JackyAIApp.Server.DTO.DataAnalysis;
using Microsoft.Data.SqlClient;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Gate for a custom data source's connection settings, run when one is saved and again whenever a
    /// stored one is turned into an engine. A custom source must authenticate with credentials its owner
    /// supplied: anything that would let the driver fall back to an identity of the server itself (its
    /// managed identity, the Azure CLI/environment chain behind ActiveDirectoryDefault, Windows auth) is
    /// refused, so a user can never point the app's own identity at a resource it happens to be granted.
    /// Each method returns the reason to refuse, or null when the settings may be used.
    /// </summary>
    public static class DataSourceConfigValidator
    {
        /// <summary>Longest login wait accepted, in seconds; 0 (wait forever) is refused too.</summary>
        internal const int MaxConnectTimeoutSeconds = 30;

        /// <summary>The only SQL authentication modes that take the owner's own User ID + Password.</summary>
        private static readonly HashSet<SqlAuthenticationMethod> AllowedSqlAuthentication =
        [
            SqlAuthenticationMethod.NotSpecified,
            SqlAuthenticationMethod.SqlPassword,
#pragma warning disable CS0618 // Deprecated by Microsoft in favour of interactive flows, but still the owner's own credentials.
            SqlAuthenticationMethod.ActiveDirectoryPassword,
#pragma warning restore CS0618
            SqlAuthenticationMethod.ActiveDirectoryServicePrincipal,
        ];

        /// <summary>Hosts an Azure Data Explorer / Fabric Eventhouse cluster URI may name.</summary>
        private static readonly string[] AllowedKustoHostSuffixes =
        [
            ".kusto.windows.net",
            ".kusto.data.microsoft.com",
            ".kusto.fabric.microsoft.com",
            ".kustomfa.windows.net",
        ];

        public static string? Validate(MsSqlSourceConfigDto? config)
        {
            if (config is null || string.IsNullOrWhiteSpace(config.ConnectionString))
                return "A SQL Server connection string is required.";

            SqlConnectionStringBuilder builder;
            try
            {
                builder = new SqlConnectionStringBuilder(config.ConnectionString);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException)
            {
                return "The SQL Server connection string is not valid.";
            }

            if (builder.IntegratedSecurity)
                return "Integrated Security is not allowed; sign in with a User ID and Password.";
            if (!AllowedSqlAuthentication.Contains(builder.Authentication))
                return $"Authentication={builder.Authentication} is not allowed; use SQL login (SqlPassword), "
                       + "ActiveDirectoryPassword or ActiveDirectoryServicePrincipal.";
            if (string.IsNullOrWhiteSpace(builder.UserID) || string.IsNullOrWhiteSpace(builder.Password))
                return "The connection string needs both a User ID and a Password.";
            if (!string.IsNullOrWhiteSpace(builder.AttachDBFilename))
                return "AttachDBFilename is not allowed.";
            if (builder.ConnectTimeout is <= 0 or > MaxConnectTimeoutSeconds)
                return $"Connect Timeout must be between 1 and {MaxConnectTimeoutSeconds} seconds.";
            return null;
        }

        public static string? Validate(AdxSourceConfigDto? config)
        {
            if (config is null)
                return "Azure Data Explorer settings are required.";
            // Went straight into KustoConnectionStringBuilder, which can select ambient-credential modes
            // (managed identity, Azure CLI…). The UI never sends one; only the service-principal fields work.
            if (!string.IsNullOrWhiteSpace(config.ConnectionString))
                return "A raw Azure Data Explorer connection string is not supported; use the cluster URI with a service principal.";
            if (string.IsNullOrWhiteSpace(config.TenantId) || string.IsNullOrWhiteSpace(config.ClientId)
                || string.IsNullOrWhiteSpace(config.ClientSecret))
                return "Tenant ID, Client ID and Client Secret are all required.";
            return ValidateClusterUri(config.ClusterUri);
        }

        /// <summary>
        /// An https Kusto cluster address and nothing else. The library hands it to
        /// KustoConnectionStringBuilder, which parses its argument as a connection string, so a ';' or '='
        /// could smuggle in other settings — and any host outside Kusto would have the server make
        /// authenticated requests wherever the caller likes.
        /// </summary>
        private static string? ValidateClusterUri(string? clusterUri)
        {
            const string expected = "The cluster URI must be an https address of an Azure Data Explorer cluster, "
                                    + "e.g. https://mycluster.westus.kusto.windows.net.";
            if (string.IsNullOrWhiteSpace(clusterUri) || clusterUri.Any(c => c is ';' or '=' || char.IsWhiteSpace(c)))
                return expected;
            if (!Uri.TryCreate(clusterUri, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
                || uri.HostNameType != UriHostNameType.Dns
                || !AllowedKustoHostSuffixes.Any(s => uri.Host.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                return expected;
            return null;
        }
    }
}
