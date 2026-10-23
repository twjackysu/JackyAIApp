using System.Net;
using System.Net.Sockets;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// Which addresses the server may connect to on a user's behalf — an MCP server URL a user typed
    /// in, and the OAuth endpoints it points at. Only public internet addresses: a URL that resolves
    /// to this machine, the private network, the cloud's metadata service (169.254.169.254) or any
    /// other address not routed on the internet would let a user reach what only the server can
    /// (SSRF). Checked on the address actually connected to, after DNS, see <see cref="SafeHttp"/>.
    /// </summary>
    public static class OutboundAddressPolicy
    {
        /// <summary>True when the address is a public, routable unicast address.</summary>
        public static bool IsAllowed(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

            return address.AddressFamily switch
            {
                AddressFamily.InterNetwork => IsPublicV4(address.GetAddressBytes()),
                AddressFamily.InterNetworkV6 => IsPublicV6(address),
                _ => false,
            };
        }

        private static bool IsPublicV4(byte[] b) => !(
            b[0] == 0                                        // 0.0.0.0/8 "this network"
            || b[0] == 10                                    // 10/8 private
            || b[0] == 127                                   // loopback
            || (b[0] == 100 && (b[1] & 0xC0) == 64)          // 100.64/10 carrier-grade NAT
            || (b[0] == 169 && b[1] == 254)                  // link-local, cloud metadata
            || (b[0] == 172 && (b[1] & 0xF0) == 16)          // 172.16/12 private
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)       // 192.0.0/24 IETF protocol assignments
            || (b[0] == 192 && b[1] == 0 && b[2] == 2)       // 192.0.2/24 documentation
            || (b[0] == 192 && b[1] == 88 && b[2] == 99)     // 6to4 relay anycast (deprecated)
            || (b[0] == 192 && b[1] == 168)                  // 192.168/16 private
            || (b[0] == 198 && (b[1] & 0xFE) == 18)          // 198.18/15 benchmarking
            || (b[0] == 198 && b[1] == 51 && b[2] == 100)    // 198.51.100/24 documentation
            || (b[0] == 203 && b[1] == 0 && b[2] == 113)     // 203.0.113/24 documentation
            || b[0] >= 224);                                 // multicast, reserved, broadcast

        private static bool IsPublicV6(IPAddress address)
        {
            if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback)) return false;
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;

            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;          // fc00::/7 unique local
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false; // 2001:db8::/32 documentation
            if (b[0] == 0x20 && b[1] == 0x02) return false;   // 2002::/16 6to4: wraps an IPv4 address of any kind
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B) return false; // 64:ff9b::/96 NAT64
            // IPv4-compatible (::a.b.c.d, deprecated): the first 96 bits are zero.
            if (b.Take(12).All(x => x == 0)) return false;
            // Only global unicast (2000::/3) is routed on the internet.
            return (b[0] & 0xE0) == 0x20;
        }
    }
}
