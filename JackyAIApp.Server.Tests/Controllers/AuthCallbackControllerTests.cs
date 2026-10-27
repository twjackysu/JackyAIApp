using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// The account connections' OAuth callback sends the reader back to /connectors naming only a provider the site has, as
    /// the site spells it: the route value is anyone's (D-174).
    /// </summary>
    public class AuthCallbackControllerTests
    {
        private readonly Mock<IConnectorService> _connectors = new();

        private AuthCallbackController Controller()
        {
            var http = new DefaultHttpContext();
            http.Request.Host = new HostString("apps.jacky-studio.com");
            return new AuthCallbackController(_connectors.Object, NullLogger<AuthCallbackController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = http },
            };
        }

        [Theory]
        [InlineData("Google&success=true")]
        [InlineData("Google#x")]
        [InlineData("evil.example")]
        public async Task AProviderTheSiteDoesNotHave_IsNotWrittenIntoTheAddress(string provider)
        {
            var result = await Controller().HandleCallback(provider, code: null, state: null, error: "access_denied", error_description: null);

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://apps.jacky-studio.com/connectors?error=invalid_provider", redirect.Url);
        }

        [Fact]
        public async Task AProvider_IsWrittenAsTheSiteSpellsIt()
        {
            var result = await Controller().HandleCallback("google", code: null, state: null, error: "access denied", error_description: null);

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://apps.jacky-studio.com/connectors?error=access%20denied&provider=Google", redirect.Url);
        }

        [Fact]
        public async Task ASignIn_ThatWorked_SaysSo()
        {
            _connectors.Setup(c => c.HandleCallbackAsync("Atlassian", "code-1", "state-1")).ReturnsAsync(true);

            var result = await Controller().HandleCallback("ATLASSIAN", code: "code-1", state: "state-1", error: null, error_description: null);

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://apps.jacky-studio.com/connectors?success=true&provider=Atlassian", redirect.Url);
        }
    }
}
