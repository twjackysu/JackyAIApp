using JackyAIApp.Server.Data;
using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services.ExternalApi;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TWStockLib.Services;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    /// <summary>The 回落煞車 pieces are wired into DI the way the app will resolve them.</summary>
    public class DrawdownBrakeRegistrationTests
    {
        private static ServiceProvider BuildProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddDbContextFactory<AzureSQLDBContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddPortfolioServices();
            services.AddTwseOpenApi(); // the TW listed-company list reads TWSE through it (registered beside it in Program)
            return services.BuildServiceProvider();
        }

        [Fact]
        public void EverythingTheEndpointNeeds_Resolves()
        {
            using var provider = BuildProvider();

            Assert.NotNull(provider.GetRequiredService<IDrawdownBrakeService>());
            Assert.NotNull(provider.GetRequiredService<IDrawdownBrakeAccess>());
            Assert.NotNull(provider.GetRequiredService<ITaiexTotalReturnStore>());
            Assert.NotNull(provider.GetRequiredService<ITradingCalendar>());
        }

        [Fact]
        public void TheCalendar_IsLoadedInTheBackground_AndTheSameOneEverywhere()
        {
            using var provider = BuildProvider();

            Assert.Same(provider.GetRequiredService<TwseTradingCalendar>(), provider.GetRequiredService<ITradingCalendar>());
            Assert.Contains(provider.GetServices<IHostedService>(), s => s is TradingCalendarRefreshService);
        }

        [Fact]
        public void TheTelemetry_IsASingleton_SoThereIsOneMeterAndOneSalt()
        {
            using var provider = BuildProvider();

            var telemetry = provider.GetRequiredService<DrawdownBrakeTelemetry>();

            Assert.Same(telemetry, provider.GetRequiredService<DrawdownBrakeTelemetry>());
            Assert.Equal(DrawdownBrakeTelemetry.MeterName, telemetry.Meter.Name);
        }

        [Fact]
        public void TheClient_IsASingleton_SoTheThreeSecondGapIsProcessWide()
        {
            using var provider = BuildProvider();

            Assert.Same(
                provider.GetRequiredService<ITaiexTotalReturnClient>(),
                provider.GetRequiredService<ITaiexTotalReturnClient>());
        }

        [Fact]
        public void TheRefreshService_IsRegisteredAsAHostedService_AndCanBeBuilt()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddDbContextFactory<AzureSQLDBContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddPortfolioServices();
            services.AddTwseOpenApi(); // the TW listed-company list reads TWSE through it (registered beside it in Program)

            Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));

            using var provider = services.BuildServiceProvider();
            Assert.Contains(provider.GetServices<IHostedService>(), h => h is TaiexTotalReturnRefreshService);
        }

        [Fact]
        public void TheClientsHttpClient_HasNoRetryHandler_SoAFailedGetIsNeverResentWithinTheMinimumGap()
        {
            // AddReadOnlyResilience adds a Polly retry (about 1s and 2s later), which would break the
            // 3-second spacing and re-send into a rate limit. The named client must not carry it.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddDbContextFactory<AzureSQLDBContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddPortfolioServices();
            services.AddTwseOpenApi(); // the TW listed-company list reads TWSE through it (registered beside it in Program)
            services.AddHttpClient("WithResilience").AddReadOnlyResilience(); // the control: what the retry looks like when present
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>();

            Assert.NotEmpty(options.Get("WithResilience").HttpMessageHandlerBuilderActions);
            Assert.Empty(options.Get(TaiexTotalReturnClient.HttpClientName).HttpMessageHandlerBuilderActions);
        }

        [Fact]
        public void TheHourlyLoopAndTheManualRefresh_AreTheSameObject_SoTheyShareOneGateAndCooldown()
        {
            using var provider = BuildProvider();

            var hosted = provider.GetServices<IHostedService>().OfType<TaiexTotalReturnRefreshService>().Single();

            Assert.Same(hosted, provider.GetRequiredService<ITaiexTotalReturnRefresher>());
            Assert.Same(hosted, provider.GetRequiredService<TaiexTotalReturnRefreshService>());
        }

        [Fact]
        public void TheRefreshAndTheSignal_ShareOneStatus()
        {
            using var provider = BuildProvider();

            Assert.Same(provider.GetRequiredService<ITaiexDataStatus>(), provider.GetRequiredService<ITaiexDataStatus>());
            Assert.IsType<DrawdownBrakeService>(provider.GetRequiredService<IDrawdownBrakeService>());
        }

        [Fact]
        public void TheMisPieces_AreSingletons_SoThereIsOneSharedReadingForTheWholeSite()
        {
            using var provider = BuildProvider();

            var source = provider.GetRequiredService<IMisTaiexQuoteSource>();

            Assert.IsType<MisTaiexQuoteSource>(source);
            Assert.Same(source, provider.GetRequiredService<IMisTaiexQuoteSource>());
            Assert.IsType<MisTaiexClient>(provider.GetRequiredService<IMisTaiexClient>());
            Assert.Same(provider.GetRequiredService<IMisTaiexClient>(), provider.GetRequiredService<IMisTaiexClient>());
        }

        [Fact]
        public void TheMisSingletons_DoNotCaptureTheLibrarysScopedService()
        {
            // TWStockLib registers IStockMarketService as scoped; the MIS client is a singleton and must
            // reach it through a scope per call. With scope validation on, capturing it would throw on resolve.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddDbContextFactory<AzureSQLDBContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddPortfolioServices();
            services.AddTwseOpenApi(); // the TW listed-company list reads TWSE through it (registered beside it in Program)
            services.AddTwStock();
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            Assert.NotNull(provider.GetRequiredService<IMisTaiexClient>());
            Assert.NotNull(provider.GetRequiredService<IMisTaiexQuoteSource>());
            Assert.NotNull(provider.GetRequiredService<IDrawdownBrakeService>());
        }
    }
}
