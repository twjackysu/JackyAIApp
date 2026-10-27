using System.Net;
using System.Text;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// An account connection's expired token is refreshed once when two requests find it so together (D-174): a provider
    /// that rotates refresh tokens (Atlassian) refuses the same one spent twice, and may end the connection.
    /// </summary>
    public class ConnectorServiceRefreshTests
    {
        private readonly string _dbName = Guid.NewGuid().ToString();
        // Each test its own reader: the refresh lock is per reader and provider, in the process.
        private readonly string _userId = "u-" + Guid.NewGuid().ToString("N");
        private readonly TokenEndpoint _endpoint = new();

        private AzureSQLDBContext NewContext() => new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(_dbName).Options);

        private ConnectorService Service(AzureSQLDBContext db)
        {
            var encryption = new Mock<ITokenEncryptionService>();
            encryption.Setup(e => e.EncryptToken(It.IsAny<string>())).Returns<string>(t => "enc:" + t);
            encryption.Setup(e => e.DecryptToken(It.IsAny<string>())).Returns<string>(t => t.Replace("enc:", ""));
            var options = Microsoft.Extensions.Options.Options.Create(new ConnectorOptions
            {
                CallbackBaseUrl = "https://apps.example.com/api/connectors/callback",
                Providers = new()
                {
                    [ConnectorProviders.Atlassian] = new ProviderConfig { ClientId = "c", ClientSecret = "s", TokenUrl = "https://auth.example.com/token" },
                },
            });
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(_userId);
            return new ConnectorService(db, encryption.Object, Mock.Of<IStateService>(), options, new HttpClient(_endpoint),
                NullLogger<ConnectorService>.Instance, user.Object);
        }

        private async Task SeedExpiredAsync()
        {
            using var db = NewContext();
            db.UserConnectors.Add(new UserConnector
            {
                UserId = _userId,
                ProviderName = ConnectorProviders.Atlassian,
                EncryptedAccessToken = "enc:a1",
                EncryptedRefreshToken = "enc:r1",
                TokenExpiresAt = DateTime.UtcNow.AddMinutes(-1),
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task TwoRequestsFindingItExpired_RefreshItOnce_AndBothGetTheNewToken()
        {
            await SeedExpiredAsync();
            using var first = NewContext();
            using var second = NewContext();

            var a = Service(first).GetAccessTokenAsync(_userId, ConnectorProviders.Atlassian);
            var b = Service(second).GetAccessTokenAsync(_userId, ConnectorProviders.Atlassian);
            await _endpoint.FirstRequest.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Give the second request time to reach the lock while the first is still at the provider.
            await Task.Delay(100);
            _endpoint.Answer.SetResult();

            Assert.Equal("a2", await a);
            Assert.Equal("a2", await b);
            Assert.Equal(new[] { "r1" }, _endpoint.RefreshTokensSpent);
        }

        [Fact]
        public async Task ARefreshTheProviderRefuses_IsReportedAsFailed()
        {
            await SeedExpiredAsync();
            _endpoint.Refuse = true;
            _endpoint.Answer.SetResult();
            using var db = NewContext();

            Assert.Null(await Service(db).GetAccessTokenAsync(_userId, ConnectorProviders.Atlassian));
            Assert.Equal(new[] { "r1" }, _endpoint.RefreshTokensSpent);
        }

        /// <summary>A token endpoint that rotates refresh tokens and answers only once the test lets it.</summary>
        private sealed class TokenEndpoint : HttpMessageHandler
        {
            private readonly object _gate = new();
            public List<string> RefreshTokensSpent { get; } = [];
            public TaskCompletionSource FirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Refuse { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var form = await request.Content!.ReadAsStringAsync(ct);
                var spent = form.Split('&').Select(Uri.UnescapeDataString)
                    .First(p => p.StartsWith("refresh_token=", StringComparison.Ordinal))["refresh_token=".Length..];
                lock (_gate) RefreshTokensSpent.Add(spent);
                FirstRequest.TrySetResult();
                await Answer.Task;
                if (Refuse)
                    return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}") };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"a2","refresh_token":"r2","expires_in":3600}""", Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}
