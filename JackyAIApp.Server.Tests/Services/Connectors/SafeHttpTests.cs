using System.Net;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>
    /// A URL a user supplies (an MCP server) must not reach anything only the server can: this
    /// machine, the private network, the cloud's metadata service, or any address not routed on the
    /// internet — whichever way the address is written, and whatever its name resolves to.
    /// </summary>
    public class SafeHttpTests
    {
        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("127.255.255.254")]
        [InlineData("0.0.0.0")]
        [InlineData("10.1.2.3")]
        [InlineData("172.16.0.1")]
        [InlineData("172.31.255.255")]
        [InlineData("192.168.1.1")]
        [InlineData("169.254.169.254")] // the cloud metadata service
        [InlineData("168.63.129.16")]   // Azure's platform address
        [InlineData("100.64.0.1")]
        [InlineData("100.127.255.255")]
        [InlineData("192.0.0.8")]
        [InlineData("192.0.2.1")]
        [InlineData("198.18.0.1")]
        [InlineData("198.51.100.7")]
        [InlineData("203.0.113.9")]
        [InlineData("224.0.0.1")]
        [InlineData("239.255.255.250")]
        [InlineData("255.255.255.255")]
        [InlineData("::1")]
        [InlineData("::")]
        [InlineData("fe80::1")]
        [InlineData("fc00::1")]
        [InlineData("fd12:3456::1")]
        [InlineData("ff02::1")]
        [InlineData("2001:db8::1")]
        [InlineData("::ffff:127.0.0.1")]   // IPv4-mapped loopback
        [InlineData("::ffff:169.254.169.254")]
        [InlineData("::ffff:10.0.0.1")]
        [InlineData("::127.0.0.1")]        // IPv4-compatible (deprecated)
        [InlineData("2002:7f00:1::1")]     // 6to4 wrapping 127.0.0.1
        [InlineData("64:ff9b::a00:1")]     // NAT64 wrapping 10.0.0.1
        public void AnAddressNotOnThePublicInternet_IsRefused(string address)
        {
            Assert.False(OutboundAddressPolicy.IsAllowed(IPAddress.Parse(address)));
        }

        [Theory]
        [InlineData("8.8.8.8")]
        [InlineData("1.1.1.1")]
        [InlineData("172.15.255.255")]  // just below 172.16/12
        [InlineData("172.32.0.1")]      // just above it
        [InlineData("100.63.255.255")]  // just below 100.64/10
        [InlineData("100.128.0.1")]     // just above it
        [InlineData("2606:4700:4700::1111")]
        [InlineData("::ffff:8.8.8.8")]  // IPv4-mapped public
        public void APublicAddress_IsAllowed(string address)
        {
            Assert.True(OutboundAddressPolicy.IsAllowed(IPAddress.Parse(address)));
        }

        [Theory]
        [InlineData("http://127.0.0.1:9/")]
        [InlineData("http://[::1]:9/")]
        [InlineData("http://169.254.169.254/latest/meta-data/")]
        public async Task ARequestToAPrivateAddress_IsRefusedBeforeConnecting(string url)
        {
            using var client = new HttpClient(SafeHttp.CreateHandler(NoDns));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));

            Assert.IsType<BlockedAddressException>(ex.InnerException);
        }

        [Theory]
        [InlineData("http://2130706433:9/")]   // 127.0.0.1 written as one number
        [InlineData("http://0x7f.1:9/")]       // 127.0.0.1 in hex shorthand
        public async Task AnOddlyWrittenLoopbackAddress_IsRefused(string url)
        {
            // Read as an address (by the URI parser or IPAddress) it is loopback; read as a name, it
            // resolves to nothing here. Refused either way, never connected.
            using var client = new HttpClient(SafeHttp.CreateHandler(Resolving()));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));

            Assert.IsType<BlockedAddressException>(ex.InnerException);
        }

        [Theory]
        [InlineData("10.0.0.5")]
        // AAAA answers: a unique local, a link-local, loopback, and an IPv4-mapped metadata address.
        [InlineData("fd00::1")]
        [InlineData("fe80::1")]
        [InlineData("::1")]
        [InlineData("::ffff:169.254.169.254")]
        public async Task AHostThatResolvesToAPrivateAddress_IsRefused(string address)
        {
            using var client = new HttpClient(SafeHttp.CreateHandler(Resolving(address)));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://mcp.example.com/mcp"));

            var blocked = Assert.IsType<BlockedAddressException>(ex.InnerException);
            Assert.Equal("mcp.example.com", blocked.Host);
        }

        [Fact]
        public async Task AHostWithAnyPrivateAddressAmongItsAnswers_IsRefused()
        {
            // A public answer next to a private one could be used to get the private one connected.
            using var client = new HttpClient(SafeHttp.CreateHandler(Resolving("8.8.8.8", "127.0.0.1")));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://mcp.example.com/mcp"));

            Assert.IsType<BlockedAddressException>(ex.InnerException);
        }

        [Fact]
        public async Task AHostThatDoesNotResolve_IsRefused()
        {
            using var client = new HttpClient(SafeHttp.CreateHandler(Resolving()));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://nowhere.example.com/"));

            Assert.IsType<BlockedAddressException>(ex.InnerException);
        }

        [Fact]
        public void TheHandler_FollowsNoRedirect_AndKeepsNoCookie()
        {
            using var handler = SafeHttp.CreateHandler();

            Assert.False(handler.AllowAutoRedirect);
            Assert.False(handler.UseCookies);
        }

        private static Task<IPAddress[]> NoDns(string host, CancellationToken ct) =>
            throw new InvalidOperationException($"'{host}' should not have needed DNS");

        private static Func<string, CancellationToken, Task<IPAddress[]>> Resolving(params string[] addresses) =>
            (_, _) => Task.FromResult(addresses.Select(a => IPAddress.Parse(a)).ToArray());
    }
}
