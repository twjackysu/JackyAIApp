using System.Reflection;
using System.Security.Claims;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>
    /// What makes the server reach a host a reader named is held per reader (D-155): a press in the add dialog or on a
    /// connector stays well inside the limit, a loop does not.
    /// </summary>
    public class ConnectorProbeRateLimitTests
    {
        private static HttpContext Reader(string userId) => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Cookies")),
        };

        [Fact]
        public void EachReaderHasTheirOwnWindow_WhichAllowsItsPermitsThenRefuses()
        {
            var alice = RateLimitPolicies.ConnectorProbePartition(Reader("alice"));
            var bob = RateLimitPolicies.ConnectorProbePartition(Reader("bob"));
            Assert.NotEqual(alice.PartitionKey, bob.PartitionKey);

            using var limiter = alice.Factory(alice.PartitionKey);
            for (var i = 0; i < RateLimitPolicies.ConnectorProbePermitsPerMinute; i++)
                Assert.True(limiter.AttemptAcquire().IsAcquired, $"permit {i + 1}");
            Assert.False(limiter.AttemptAcquire().IsAcquired);
        }

        [Theory]
        [InlineData(nameof(AiConnectorsController.Probe))]
        [InlineData(nameof(AiConnectorsController.Create))]
        [InlineData(nameof(AiConnectorsController.Test))]
        public void TheEndpointsThatReachAServer_AreHeldToIt(string action)
        {
            var method = typeof(AiConnectorsController).GetMethod(action)!;

            Assert.Equal(RateLimitPolicies.ConnectorProbe, method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        }
    }
}
