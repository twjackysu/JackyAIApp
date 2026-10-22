using Microsoft.Extensions.Caching.Memory;
using JackyAIApp.Server.Services;
using System.Runtime.CompilerServices;
using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.Finance.Builder;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Services.Finance.Indicators;
using JackyAIApp.Server.Services.Finance.Prediction;
using JackyAIApp.Server.Services.Finance.Scoring;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// GET api/finance/market-index-history, top to bottom: the controller (anonymous, unmetered, no
    /// parameters), the query service (no data is an error, never an empty success), the JSON a client
    /// receives, the DI wiring that makes the cache one copy for everyone, and response compression.
    /// </summary>
    public class FinanceControllerMarketIndexHistoryTests
    {
        private static MarketIndexHistoryResponse Sample() => new(
            "^TWII", "加權指數（價格指數，不含息）", "Yahoo Finance", new DateOnly(2026, 10, 8),
            new[]
            {
                new MarketIndexPoint(new DateOnly(2021, 1, 4), 14138.24m),
                new MarketIndexPoint(new DateOnly(2026, 10, 8), 28000.5m),
            });

        // ---- Controller ------------------------------------------------------------------------

        private readonly Mock<IMyResponseFactory> _responseFactory = new();
        private readonly Mock<IMarketDataQueryService> _query = new();
        private readonly FinanceController _controller;

        public FinanceControllerMarketIndexHistoryTests()
        {
            _responseFactory.Setup(f => f.CreateOKResponse(It.IsAny<object?>()))
                .Returns<object?>(data => new OkObjectResult(data));
            _responseFactory.Setup(f => f.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                .Returns<ErrorCodes, string>((code, msg) => new ObjectResult(new { code, msg }) { StatusCode = (int)code / 100 % 1000 });

            _controller = new FinanceController(
                _responseFactory.Object, Mock.Of<IStockAnalysisQueryService>(), _query.Object,
                Mock.Of<IMarketInsightService>(), new Mock<IServiceScopeFactory>().Object,
                NullLogger<FinanceController>.Instance);
        }

        [Fact]
        public async Task Success_Returns200WithTheSeries()
        {
            var sample = Sample();
            _query.Setup(q => q.GetMarketIndexHistoryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(sample));

            var result = await _controller.GetMarketIndexHistory(CancellationToken.None);

            Assert.Same(sample, Assert.IsType<OkObjectResult>(result).Value);
        }

        [Fact]
        public async Task NoData_Returns503WithTheDedicatedErrorCode()
        {
            _query.Setup(q => q.GetMarketIndexHistoryAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.MarketHistoryUnavailable, "unavailable"));

            var result = await _controller.GetMarketIndexHistory(CancellationToken.None);

            Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
            _responseFactory.Verify(f => f.CreateErrorResponse(ErrorCodes.MarketHistoryUnavailable, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void TheEndpoint_IsAnonymous_Unmetered_AndTakesNoParameters()
        {
            var action = typeof(FinanceController).GetMethod(nameof(FinanceController.GetMarketIndexHistory))!;

            // /finance has always been open to visitors, and market-overview next to it is too: neither
            // the controller nor the action may demand a login.
            Assert.Empty(typeof(FinanceController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
            Assert.Empty(action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
            // No AI behind it, so nothing to meter.
            Assert.Empty(action.GetCustomAttributes(typeof(UsageLimitAttribute), inherit: true));
            // Only a cancellation token: a symbol/range argument would turn this into a Yahoo proxy.
            Assert.Collection(action.GetParameters(), p => Assert.Equal(typeof(CancellationToken), p.ParameterType));

            var route = Assert.Single(action.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Cast<HttpGetAttribute>());
            Assert.Equal("market-index-history", route.Template);
        }

        [Fact]
        public void TheUnavailableErrorCode_MapsToHttp503()
        {
            // ResponseFactory derives the HTTP status from digits 3-5 of the code.
            Assert.Equal(503, (int)ErrorCodes.MarketHistoryUnavailable / 100 % 1000);
        }

        [Fact]
        public void TheErrorCodes_AreAllDistinct()
        {
            var values = Enum.GetValues<ErrorCodes>().Select(c => (int)c).ToList();
            Assert.Equal(values.Count, values.Distinct().Count());
        }

        // ---- Query service ---------------------------------------------------------------------

        private static MarketDataQueryService QueryServiceOver(IMarketIndexHistoryProvider history) => new(
            NullLogger<MarketDataQueryService>.Instance,
            new MemoryCache(new MemoryCacheOptions()),
            new Mock<IMacroEconomyProvider>().Object,
            new Mock<IMarketOverviewProvider>().Object,
            history,
            new Mock<IIndicatorProvider>().Object);

        [Fact]
        public async Task QueryService_PassesTheSeriesThrough()
        {
            var sample = Sample();
            var history = new Mock<IMarketIndexHistoryProvider>();
            history.Setup(h => h.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(sample);

            var result = await QueryServiceOver(history.Object).GetMarketIndexHistoryAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Same(sample, result.Data);
        }

        [Fact]
        public async Task QueryService_NoData_IsAnError_NotAnEmptySuccess()
        {
            var history = new Mock<IMarketIndexHistoryProvider>();
            history.Setup(h => h.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((MarketIndexHistoryResponse?)null);

            var result = await QueryServiceOver(history.Object).GetMarketIndexHistoryAsync(CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.MarketHistoryUnavailable, result.Error);
            Assert.Null(result.Data);
        }

        [Fact]
        public async Task QueryService_HandsTheCallersTokenToTheProvider()
        {
            using var cts = new CancellationTokenSource();
            var history = new Mock<IMarketIndexHistoryProvider>();
            history.Setup(h => h.GetAsync(cts.Token)).ReturnsAsync(Sample());

            await QueryServiceOver(history.Object).GetMarketIndexHistoryAsync(cts.Token);

            history.Verify(h => h.GetAsync(cts.Token), Times.Once);
        }

        // ---- The JSON a client receives --------------------------------------------------------

        [Fact]
        public void Json_MatchesTheContractTheFrontendBuildsAgainst()
        {
            // The same options MVC uses (web defaults = camelCase), plus the ones Program.cs adds.
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            };
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(Sample(), options));
            var root = doc.RootElement;

            Assert.Equal(
                new[] { "symbol", "name", "source", "asOf", "points" },
                root.EnumerateObject().Select(p => p.Name));
            Assert.Equal("^TWII", root.GetProperty("symbol").GetString());
            Assert.Equal("加權指數（價格指數，不含息）", root.GetProperty("name").GetString());
            Assert.Equal("Yahoo Finance", root.GetProperty("source").GetString());
            Assert.Equal("2026-10-08", root.GetProperty("asOf").GetString());

            var points = root.GetProperty("points");
            Assert.Equal(2, points.GetArrayLength());
            Assert.Equal(new[] { "date", "close" }, points[0].EnumerateObject().Select(p => p.Name));
            Assert.Equal("2021-01-04", points[0].GetProperty("date").GetString());
            Assert.Equal(14138.24m, points[0].GetProperty("close").GetDecimal());
        }

        // ---- DI: one cache, shared by everyone -------------------------------------------------

        [Fact]
        public void Registration_ResolvesTheCachedProvider_AsOneInstanceAcrossRequests()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddHttpClient();
            services.AddFinanceAnalysisServices();

            using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scopeA = root.CreateScope();
            using var scopeB = root.CreateScope();

            var a = scopeA.ServiceProvider.GetRequiredService<IMarketIndexHistoryProvider>();
            var b = scopeB.ServiceProvider.GetRequiredService<IMarketIndexHistoryProvider>();

            Assert.IsType<CachedMarketIndexHistoryProvider>(a);
            Assert.Same(a, b); // a per-request instance would still share the memory cache, but pay for its own wiring
            Assert.Same(a, root.GetRequiredService<IMarketIndexHistoryProvider>());
        }

        // ---- Response compression --------------------------------------------------------------

        [Fact]
        public void JsonResponses_AreCompressed()
        {
            // Same registration as Program.cs (AddResponseCompression with EnableForHttps). The series is
            // about 1,450 points, ~40 KB raw; it only costs ~10 KB on the wire if application/json is in the list.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddResponseCompression(options => options.EnableForHttps = true);
            using var sp = services.BuildServiceProvider();
            var provider = sp.GetRequiredService<IResponseCompressionProvider>();

            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Headers.AcceptEncoding = "gzip, deflate, br";
            context.Response.ContentType = "application/json; charset=utf-8";

            Assert.True(provider.CheckRequestAcceptsCompression(context));
            Assert.True(provider.ShouldCompressResponse(context));
            Assert.NotNull(provider.GetCompressionProvider(context));
        }
    }
}
