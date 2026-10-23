using System.Net;
using System.Net.Sockets;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// The HTTP handler for requests to addresses a user supplied (an MCP server, the OAuth endpoints
    /// its metadata names). It resolves the host itself and connects only when every address the name
    /// resolves to is public (<see cref="OutboundAddressPolicy"/>), then connects to exactly the
    /// addresses it checked — so a DNS answer that changes between a check and the connection (DNS
    /// rebinding) cannot slip a private address in. Each connection is checked, a redirect's
    /// included; redirects are not followed automatically, cookies are not kept.
    /// </summary>
    public static class SafeHttp
    {
        /// <param name="resolve">How a host name is resolved; the system's DNS unless a test supplies one.</param>
        public static SocketsHttpHandler CreateHandler(Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
        {
            resolve ??= (host, ct) => Dns.GetHostAddressesAsync(host, ct);
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                ConnectCallback = async (context, ct) =>
                {
                    // An IPv6 literal may come bracketed ("[::1]").
                    var host = context.DnsEndPoint.Host.Trim('[', ']');
                    var addresses = IPAddress.TryParse(host, out var literal) ? new[] { literal } : await resolve(host, ct);
                    if (addresses.Length == 0 || !addresses.All(OutboundAddressPolicy.IsAllowed))
                        throw new BlockedAddressException(host);

                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            };
        }
    }

    /// <summary>A request was refused because its host is, or resolves to, an address that is not public.</summary>
    public sealed class BlockedAddressException(string host)
        : IOException($"'{host}' is not a public internet address.")
    {
        public string Host { get; } = host;
    }
}
