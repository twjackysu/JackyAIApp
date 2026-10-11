using System.Diagnostics;
using System.Net;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    public class TaiexTotalReturnClientTests
    {
        private const string OctoberJson =
            "{\"stat\":\"OK\",\"fields\":[\"日期\",\"發行量加權股價報酬指數\"],\"data\":[[\"115/10/01\",\"41,234.56\"]]}";

        private static TaiexTotalReturnClient CreateClient(HttpMessageHandler handler, TimeSpan? minInterval = null)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(TaiexTotalReturnClient.HttpClientName))
                .Returns(() => new HttpClient(handler, disposeHandler: false));
            return new TaiexTotalReturnClient(factory.Object, NullLogger<TaiexTotalReturnClient>.Instance, minInterval: minInterval ?? TimeSpan.Zero);
        }

        [Fact]
        public async Task FetchMonthAsync_RequestsTheMonthsFirstDay_AndParsesIt()
        {
            Uri? requested = null;
            var client = CreateClient(new StubHttpMessageHandler(req =>
            {
                requested = req.RequestUri;
                return StubHttpMessageHandler.JsonResponse(OctoberJson);
            }));

            var result = await client.FetchMonthAsync(2026, 10);

            Assert.Equal("https://www.twse.com.tw/rwd/zh/TAIEX/MFI94U?date=20261001&response=json", requested!.ToString());
            Assert.True(result.Success);
            Assert.Equal(new IndexClose(new DateOnly(2026, 10, 1), 41234.56m), Assert.Single(result.Points));
        }

        [Fact]
        public async Task FetchMonthAsync_PadsASingleDigitMonth()
        {
            Uri? requested = null;
            var client = CreateClient(new StubHttpMessageHandler(req =>
            {
                requested = req.RequestUri;
                return StubHttpMessageHandler.JsonResponse("{\"stat\":\"沒有資料\"}");
            }));

            await client.FetchMonthAsync(2024, 3);

            Assert.Contains("date=20240301", requested!.Query);
        }

        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task FetchMonthAsync_HttpError_IsAFailure_NotAnException(HttpStatusCode status)
        {
            var client = CreateClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(status)));

            Assert.False((await client.FetchMonthAsync(2026, 10)).Success);
        }

        [Fact]
        public async Task FetchMonthAsync_NetworkError_IsAFailure_NotAnException()
        {
            var client = CreateClient(new StubHttpMessageHandler(_ => throw new HttpRequestException("connection reset")));

            Assert.False((await client.FetchMonthAsync(2026, 10)).Success);
        }

        [Fact]
        public async Task FetchMonthAsync_HtmlInsteadOfJson_IsAFailure()
        {
            var client = CreateClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>too many requests</html>"),
            }));

            Assert.False((await client.FetchMonthAsync(2026, 10)).Success);
        }

        [Fact]
        public async Task FetchMonthAsync_Cancelled_Throws()
        {
            var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.JsonResponse(OctoberJson)));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchMonthAsync(2026, 10, cts.Token));
        }

        [Fact]
        public void TheProductionSpacingIsThreeSeconds() =>
            Assert.Equal(TimeSpan.FromSeconds(3), TaiexTotalReturnClient.DefaultMinInterval);

        [Fact]
        public async Task FetchMonthAsync_ConcurrentCalls_RunOneAtATime_WithTheMinimumGapBetweenThem()
        {
            var gap = TimeSpan.FromMilliseconds(150);
            var handler = new RecordingHandler(OctoberJson, responseTime: TimeSpan.FromMilliseconds(30));
            var client = CreateClient(handler, gap);

            await Task.WhenAll(Enumerable.Range(1, 4).Select(m => client.FetchMonthAsync(2026, m)));

            Assert.Equal(1, handler.MaxInFlight);
            Assert.Equal(4, handler.Spans.Count);
            for (var i = 1; i < handler.Spans.Count; i++)
            {
                var between = handler.Spans[i].Start - handler.Spans[i - 1].End;
                // The gap is measured from the previous response; allow a little scheduler slack
                // below the nominal value but never the response time or less.
                Assert.True(between >= gap - TimeSpan.FromMilliseconds(20),
                    $"request {i} started {between.TotalMilliseconds:F0}ms after the previous one finished; wanted {gap.TotalMilliseconds}ms");
            }
        }

        private sealed class RecordingHandler(string body, TimeSpan responseTime) : HttpMessageHandler
        {
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private int _inFlight;
            public int MaxInFlight { get; private set; }
            public List<(TimeSpan Start, TimeSpan End)> Spans { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var start = _clock.Elapsed;
                var now = Interlocked.Increment(ref _inFlight);
                lock (Spans) MaxInFlight = Math.Max(MaxInFlight, now);

                await Task.Delay(responseTime, cancellationToken);

                Interlocked.Decrement(ref _inFlight);
                lock (Spans) Spans.Add((start, _clock.Elapsed));
                return StubHttpMessageHandler.JsonResponse(body);
            }
        }
    }
}
