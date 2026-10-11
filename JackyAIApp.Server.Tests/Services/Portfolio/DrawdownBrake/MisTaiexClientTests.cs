using System.Net;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    public class MisTaiexClientTests
    {
        private static MisTaiexClient CreateClient(
            HttpMessageHandler handler, TimeProvider? time = null, TimeSpan? timeout = null, Action<string>? onClient = null)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns((string name) =>
                {
                    onClient?.Invoke(name);
                    return new HttpClient(handler, disposeHandler: false);
                });
            return new MisTaiexClient(factory.Object, NullLogger<MisTaiexClient>.Instance, time, timeout);
        }

        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        [Fact]
        public async Task GetAsync_AsksForTheIndex_AndParsesTheReading()
        {
            HttpRequestMessage? sent = null;
            var client = CreateClient(new StubHttpMessageHandler(req =>
            {
                sent = req;
                return StubHttpMessageHandler.JsonResponse(MisTaiexSamples.Json());
            }));

            var quote = await client.GetAsync();

            Assert.Equal(23100.50m, quote!.Last);
            Assert.Equal(HttpMethod.Get, sent!.Method);
            Assert.StartsWith("https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_t00.tw&json=1&delay=0&_=", sent.RequestUri!.ToString());
            Assert.NotEmpty(sent.Headers.UserAgent.ToString());
        }

        [Fact]
        public async Task GetAsync_TheCacheBuster_IsTheClockInMilliseconds()
        {
            var now = new DateTimeOffset(2026, 10, 8, 5, 20, 0, TimeSpan.Zero);
            Uri? requested = null;
            var client = CreateClient(new StubHttpMessageHandler(req =>
            {
                requested = req.RequestUri;
                return StubHttpMessageHandler.JsonResponse(MisTaiexSamples.Json());
            }), new FixedTime(now));

            await client.GetAsync();

            Assert.EndsWith($"&_={now.ToUnixTimeMilliseconds()}", requested!.ToString());
        }

        [Fact]
        public async Task GetAsync_GoesThroughTheSharedReadOnlyClient()
        {
            string? name = null;
            var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.JsonResponse(MisTaiexSamples.Json())), onClient: n => name = n);

            await client.GetAsync();

            Assert.Equal("ExternalReadOnly", name);
        }

        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task GetAsync_HttpError_IsNoReading_NotAnException(HttpStatusCode status)
        {
            var client = CreateClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(status)));

            Assert.Null(await client.GetAsync());
        }

        [Fact]
        public async Task GetAsync_NetworkError_IsNoReading_NotAnException()
        {
            var client = CreateClient(new StubHttpMessageHandler(_ => throw new HttpRequestException("connection reset")));

            Assert.Null(await client.GetAsync());
        }

        [Fact]
        public async Task GetAsync_AnUnreadableAnswer_IsNoReading()
        {
            var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.JsonResponse("{\"msgArray\":[]}")));

            Assert.Null(await client.GetAsync());
        }

        [Fact]
        public async Task GetAsync_AnAnswerThatNeverComes_IsNoReading_OnceTheTimeoutIsUp()
        {
            var client = CreateClient(new HangingHandler(), timeout: TimeSpan.FromMilliseconds(50));

            var started = DateTime.UtcNow;
            Assert.Null(await client.GetAsync());

            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20), "the request was not cut off");
        }

        [Fact]
        public async Task GetAsync_TheCallerWalkingAway_IsNotSwallowed()
        {
            using var cts = new CancellationTokenSource();
            var client = CreateClient(new HangingHandler(), timeout: TimeSpan.FromSeconds(30));

            var pending = client.GetAsync(cts.Token);
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }

        [Fact]
        public void TheTimeout_IsShort_BecauseAPersonIsWaitingBehindIt()
        {
            Assert.True(MisTaiexClient.DefaultTimeout <= TimeSpan.FromSeconds(10));
        }

        private sealed class HangingHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("unreachable");
            }
        }
    }
}
