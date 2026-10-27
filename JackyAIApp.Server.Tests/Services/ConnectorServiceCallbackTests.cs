using System.Net;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// An account connection's callback (Google, Microsoft, Atlassian) is taken only in the browser of the
    /// reader who started the sign-in: a sign-in URL started by one reader and sent to another must not put the
    /// other reader's account into the first one's connection.
    /// </summary>
    public class ConnectorServiceCallbackTests : IDisposable
    {
        private const string Starter = "u1";
        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly TestStateService _states = new();
        private int _tokenRequests;

        public void Dispose() => _db.Dispose();

        private ConnectorService Service(string? signedIn)
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(signedIn);
            var options = Microsoft.Extensions.Options.Options.Create(new ConnectorOptions
            {
                CallbackBaseUrl = "https://apps.example.com/api/connectors/callback",
                Providers = new()
                {
                    [ConnectorProviders.Google] = new ProviderConfig { ClientId = "c", ClientSecret = "s", TokenUrl = "https://oauth.example.com/token" },
                },
            });
            // The provider refuses the code: enough to tell whether the callback got as far as asking.
            var http = StubHttpMessageHandler.CreateClient(_ =>
            {
                _tokenRequests++;
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}") };
            });
            return new ConnectorService(_db, Mock.Of<ITokenEncryptionService>(), _states, options, http,
                NullLogger<ConnectorService>.Instance, user.Object);
        }

        [Theory]
        [InlineData("u2")]
        [InlineData(null)]
        public async Task ACallbackInAnotherReadersBrowser_IsRefused_BeforeTheCodeIsTraded(string? signedIn)
        {
            var state = _states.GenerateState(Starter, ConnectorProviders.Google);

            Assert.False(await Service(signedIn).HandleCallbackAsync(ConnectorProviders.Google, "code-1", state));
            Assert.Equal(0, _tokenRequests);
            Assert.False(_db.UserConnectors.Any());
        }

        [Fact]
        public async Task TheReaderWhoStartedIt_GetsTheCodeTraded()
        {
            var state = _states.GenerateState(Starter, ConnectorProviders.Google);

            await Service(Starter).HandleCallbackAsync(ConnectorProviders.Google, "code-1", state);

            Assert.Equal(1, _tokenRequests);
        }
    }
}
