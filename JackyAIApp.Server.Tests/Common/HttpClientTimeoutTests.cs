using Betalgo.Ranul.OpenAI.Interfaces;
using JackyAIApp.Server.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// The app's HttpClient timeouts, registered as Program does: 20s for the external APIs it reads,
    /// longer for OpenAI, whose answers took up to 20s and more and were cut off there.
    /// </summary>
    public class HttpClientTimeoutTests
    {
        private static ServiceProvider Build()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            // Betalgo's registration reads its options section, as the host's configuration provides.
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddHttpClientDefaultTimeout();
            services.AddOpenAIClient("test-key");
            return services.BuildServiceProvider();
        }

        [Fact]
        public void AnyClient_WaitsTwentySeconds()
        {
            using var provider = Build();

            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ExternalReadOnly");

            Assert.Equal(TimeSpan.FromSeconds(20), client.Timeout);
        }

        [Fact]
        public void TheOpenAIClient_WaitsNinetySeconds_NotTheDefault()
        {
            using var provider = Build();

            // The typed client Betalgo's OpenAIService is built on is named after its interface.
            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IOpenAIService));

            Assert.Equal(TimeSpan.FromSeconds(90), client.Timeout);
            Assert.NotNull(provider.GetRequiredService<IOpenAIService>());
        }
    }
}
