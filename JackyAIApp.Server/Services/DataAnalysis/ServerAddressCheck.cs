using System.Net;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Whether a custom source's server, named by host, is a machine on the internet: what
    /// <see cref="DataSourceConfigValidator"/> cannot tell from the connection string alone, since a public-looking name
    /// may resolve to a private address (D-164).
    /// </summary>
    public interface IServerAddressCheck
    {
        /// <summary>True when every address <paramref name="host"/> resolves to is public; false when one is not, or it does not resolve.</summary>
        Task<bool> IsPublicAsync(string host, CancellationToken ct);
    }

    /// <summary>
    /// The check by the system's DNS, held to <see cref="OutboundAddressPolicy"/>. The SQL Server driver resolves the name
    /// again when it connects, so a DNS answer that changes in between (rebinding) is not stopped here, unlike
    /// <see cref="SafeHttp"/>, which connects to the addresses it checked; the driver has no way to be given them.
    /// </summary>
    public sealed class DnsServerAddressCheck : IServerAddressCheck
    {
        /// <summary>The longest wait for an answer; a name that takes longer counts as not resolving.</summary>
        internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

        public DnsServerAddressCheck() : this((host, ct) => Dns.GetHostAddressesAsync(host, ct)) { }

        /// <param name="resolve">How a host name is resolved (a test's own answers).</param>
        internal DnsServerAddressCheck(Func<string, CancellationToken, Task<IPAddress[]>> resolve) => _resolve = resolve;

        public async Task<bool> IsPublicAsync(string host, CancellationToken ct)
        {
            if (IPAddress.TryParse(host, out var literal)) return OutboundAddressPolicy.IsAllowed(literal);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            try
            {
                var addresses = await _resolve(host, timeout.Token);
                return addresses.Length > 0 && addresses.All(OutboundAddressPolicy.IsAllowed);
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                return false;
            }
        }
    }
}
