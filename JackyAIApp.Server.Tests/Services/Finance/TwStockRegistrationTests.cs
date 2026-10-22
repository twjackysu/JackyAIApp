using System.Net;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using TWStockLib;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// How the app registers TWStockLib (<see cref="FinanceServiceExtensions"/>): the library's own rate
    /// limit is on at its default, and its named client gets the app's timeout and retries rather than
    /// the library's 100s and none.
    /// </summary>
    public class TwStockRegistrationTests
    {
        private static ServiceProvider Build(Action<IServiceCollection>? andThen = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddFinanceAnalysisServices();
            andThen?.Invoke(services);
            return services.BuildServiceProvider();
        }

        [Fact]
        public void TheLibrarysOwnRateLimit_IsOn_ThreeRequestsInFiveSecondsPerHost()
        {
            using var provider = Build();

            // The gateway's exchange lanes are set to stay within this (ThirdPartyGatewayTests).
            var limit = provider.GetRequiredService<IOptions<TwStockOptions>>().Value.RateLimit;
            Assert.True(limit.Enabled);
            Assert.Equal(3, limit.PermitLimit);
            Assert.Equal(TimeSpan.FromSeconds(5), limit.Window);
        }

        [Fact]
        public void TheQuotePrefetcher_Resolves()
        {
            using var provider = Build(services => services.AddSingleton(Mock.Of<ITwListedCompanyService>()));
            using var scope = provider.CreateScope();

            Assert.IsType<TwQuotePrefetcher>(scope.ServiceProvider.GetRequiredService<ITwQuotePrefetcher>());
        }

        [Fact]
        public void ItsNamedClient_WaitsThirtySeconds()
        {
            using var provider = Build();

            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TwStockOptions.HttpClientName);

            Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
        }

        [Fact]
        public async Task ItsNamedClient_RetriesAFailedRead()
        {
            var calls = 0;
            using var provider = Build(services => services.AddHttpClient(TwStockOptions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_ =>
                    ++calls == 1
                        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        : StubHttpMessageHandler.JsonResponse("{}"))));

            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TwStockOptions.HttpClientName);
            using var response = await client.GetAsync("https://www.twse.com.tw/");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, calls);
        }
    }
}
