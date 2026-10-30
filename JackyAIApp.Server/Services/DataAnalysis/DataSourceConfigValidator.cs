using System.Net;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.Connectors;
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
            if (ValidateServer(builder.DataSource, required: true) is { } serverError)
                return serverError;
            if (ValidateServer(builder.FailoverPartner, required: false) is { } partnerError)
                return partnerError;
            if (!string.IsNullOrWhiteSpace(builder.EnclaveAttestationUrl))
                return "Enclave Attestation Url is not allowed.";
            return null;
        }

        /// <summary>The address protocols a server may be named with: TCP, the only one that reaches another machine.</summary>
        private static readonly string[] RefusedProtocols = ["np:", "lpc:", "admin:"];

        /// <summary>
        /// The server a custom source connects to (or its failover partner) must be a machine on the internet, named
        /// over TCP: never this server itself, a named pipe or shared memory, or an address of a private, link-local or
        /// reserved range (the cloud metadata service, the app's own network), which the driver would otherwise
        /// reach from the App Service and whose answer — refused, timed out, not SQL Server — its owner is told
        /// (D-164). A name is checked as written; one that resolves to such an address is the open item in NEXT.
        /// </summary>
        internal static string? ValidateServer(string? dataSource, bool required)
        {
            const string expected = "The server must be a SQL Server on the internet, e.g. tcp:myserver.database.windows.net,1433.";
            if (string.IsNullOrWhiteSpace(dataSource)) return required ? expected : null;
            var host = HostOf(dataSource);
            if (host is null || host is "." || host.Equals("(local)", StringComparison.OrdinalIgnoreCase)
                || host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)
                || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
                return expected;
            if (IPAddress.TryParse(host, out var address))
                return OutboundAddressPolicy.IsAllowed(address) ? null : expected;
            // A plain name: letters, digits, '-', '_' and '.', with a dot (a bare word is a machine on the local network).
            if (!host.Contains('.') || !host.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) return expected;
            return null;
        }

        /// <summary>
        /// The host part of a server as a connection string names it (<c>tcp:host\instance,port</c>, an IPv6 literal
        /// maybe bracketed), or null for another protocol, a bad port or nothing at all.
        /// </summary>
        internal static string? HostOf(string dataSource)
        {
            var server = dataSource.Trim();
            if (RefusedProtocols.Any(p => server.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return null;
            if (server.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) server = server[4..].Trim();

            var comma = server.LastIndexOf(',');
            if (comma >= 0)
            {
                if (!int.TryParse(server[(comma + 1)..].Trim(), out var port) || port is < 1 or > 65535) return null;
                server = server[..comma].Trim();
            }
            var backslash = server.IndexOf('\\');
            if (backslash >= 0) server = server[..backslash].Trim();
            if (server.StartsWith('[') && server.EndsWith(']')) server = server[1..^1];
            return server.Length == 0 ? null : server;
        }

        /// <summary>
        /// The hosts a SQL Server source connects to (its server and failover partner), for the check by name that a
        /// connection string cannot make (<see cref="IServerAddressCheck"/>); empty when it does not parse (then
        /// <see cref="Validate(MsSqlSourceConfigDto?)"/> refuses it anyway).
        /// </summary>
        public static string[] ServerHostsOf(MsSqlSourceConfigDto config)
        {
            SqlConnectionStringBuilder builder;
            try
            {
                builder = new SqlConnectionStringBuilder(config.ConnectionString);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException)
            {
                return [];
            }
            return new[] { builder.DataSource, builder.FailoverPartner }
                .Where(server => !string.IsNullOrWhiteSpace(server))
                .Select(server => HostOf(server))
                .OfType<string>()
                .ToArray();
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
            // The library may build a Kusto connection string from these too, so a ';' or '=' in one could add a
            // setting of its own (an ambient credential), as in the cluster URI.
            if (HasSettingSyntax(config.TenantId, allowSpaces: false) || HasSettingSyntax(config.ClientId, allowSpaces: false))
                return "Tenant ID and Client ID may not contain spaces, ';' or '='.";
            if (config.Database is { } database && HasSettingSyntax(database, allowSpaces: true))
                return "The database name may not contain ';', '=' or control characters.";
            return ValidateClusterUri(config.ClusterUri);
        }

        /// <summary>Whether a value holds what could end it or start another setting in a connection string.</summary>
        private static bool HasSettingSyntax(string value, bool allowSpaces) =>
            value.Any(c => c is ';' or '=' or '"' or '\'' || char.IsControl(c) || (!allowSpaces && char.IsWhiteSpace(c)));

        /// <summary>Most connections a custom source's pool may hold: a page asks a few queries at once, never more.</summary>
        internal const int MaxPoolSize = 5;

        /// <summary>Longest a custom source's query may run, in seconds; 0 (wait forever) becomes this too.</summary>
        internal const int MaxCommandTimeoutSeconds = 120;

        /// <summary>Most reconnect tries the driver makes for a broken idle connection, and most seconds between them.</summary>
        internal const int MaxConnectRetryCount = 2;
        internal const int MaxConnectRetryIntervalSeconds = 10;

        /// <summary>
        /// A validated SQL Server connection string as it is handed to the driver: the owner's settings, with what the
        /// server's own resources pay for held in bounds — no connections kept open while idle (<c>Min Pool Size</c> 0),
        /// at most <see cref="MaxPoolSize"/> in its pool, a query that ends, few reconnect tries. Otherwise a saved source
        /// could have the App Service open and keep hundreds of connections to a host of its owner's choosing. Bounding
        /// instead of refusing keeps sources saved before this check working.
        /// </summary>
        public static string Bounded(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                MinPoolSize = 0,
            };
            if (builder.MaxPoolSize > MaxPoolSize) builder.MaxPoolSize = MaxPoolSize;
            if (builder.CommandTimeout is <= 0 or > MaxCommandTimeoutSeconds) builder.CommandTimeout = MaxCommandTimeoutSeconds;
            if (builder.ConnectRetryCount > MaxConnectRetryCount) builder.ConnectRetryCount = MaxConnectRetryCount;
            if (builder.ConnectRetryInterval > MaxConnectRetryIntervalSeconds) builder.ConnectRetryInterval = MaxConnectRetryIntervalSeconds;
            return builder.ConnectionString;
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
