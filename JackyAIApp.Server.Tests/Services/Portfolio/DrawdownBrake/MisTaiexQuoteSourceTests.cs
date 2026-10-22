using JackyAIApp.Server.Services.Market;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    public class MisTaiexQuoteSourceTests
    {
        private static readonly TimeSpan View = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(10);

        private static readonly MisIndexQuote Quote =
            new(new DateOnly(2026, 10, 8), new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.FromHours(8)), 23100m, 23000m);

        private readonly Mock<IMisTaiexClient> _client = new();
        private readonly FakeTime _time = new() { Now = new DateTimeOffset(2026, 10, 8, 2, 0, 0, TimeSpan.Zero) };

        private MisTaiexQuoteSource CreateSource() => new(_client.Object, _time);

        private void ClientAnswers(MisIndexQuote? quote) =>
            _client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(quote);

        private void ClientWasAsked(int times) =>
            _client.Verify(c => c.GetAsync(It.IsAny<CancellationToken>()), Times.Exactly(times));

        [Fact]
        public async Task TheFirstCall_AsksMis_AndStampsTheTime()
        {
            ClientAnswers(Quote);

            var snapshot = await CreateSource().GetAsync(View);

            Assert.Same(Quote, snapshot.Quote);
            Assert.Equal(_time.Now, snapshot.FetchedAt);
            ClientWasAsked(1);
        }

        [Fact]
        public async Task WithinTheMaxAge_EveryoneGetsTheSameSnapshot_AndMisIsNotAskedAgain()
        {
            ClientAnswers(Quote);
            var source = CreateSource();

            var first = await source.GetAsync(View);
            _time.Now += TimeSpan.FromSeconds(29.9);
            var second = await source.GetAsync(View);

            Assert.Same(first, second);
            ClientWasAsked(1);
        }

        [Fact]
        public async Task AtTheMaxAge_MisIsAskedAgain()
        {
            ClientAnswers(Quote);
            var source = CreateSource();

            var first = await source.GetAsync(View);
            _time.Now += View;
            var second = await source.GetAsync(View);

            Assert.NotSame(first, second);
            Assert.Equal(_time.Now, second.FetchedAt);
            ClientWasAsked(2);
        }

        [Fact]
        public async Task TheRefreshButton_AcceptsOnlyAReadingYoungerThanTenSeconds()
        {
            ClientAnswers(Quote);
            var source = CreateSource();

            var first = await source.GetAsync(View);
            _time.Now += TimeSpan.FromSeconds(9.9);
            Assert.Same(first, await source.GetAsync(Refresh));
            ClientWasAsked(1);

            _time.Now += TimeSpan.FromSeconds(0.1);
            var refreshed = await source.GetAsync(Refresh);

            Assert.NotSame(first, refreshed);
            ClientWasAsked(2);
        }

        [Fact]
        public async Task AReadingTheButtonJustMade_IsGoodForViewers()
        {
            ClientAnswers(Quote);
            var source = CreateSource();

            await source.GetAsync(View);
            _time.Now += TimeSpan.FromSeconds(20);
            var refreshed = await source.GetAsync(Refresh);   // older than 10s: asks
            _time.Now += TimeSpan.FromSeconds(15);
            var viewed = await source.GetAsync(View);          // 15s old: good enough

            Assert.Same(refreshed, viewed);
            ClientWasAsked(2);
        }

        [Fact]
        public async Task PressingTheButtonOverAndOver_AsksMisOncePerTenSeconds_AtMost()
        {
            ClientAnswers(Quote);
            var source = CreateSource();

            for (var i = 0; i < 60; i++) // a minute of presses, a second apart
            {
                await source.GetAsync(Refresh);
                _time.Now += TimeSpan.FromSeconds(1);
            }

            ClientWasAsked(6);
        }

        [Fact]
        public async Task AFailure_IsHeldToo_SoAnOutageIsNotRetriedByEveryViewer()
        {
            ClientAnswers(null);
            var source = CreateSource();

            var first = await source.GetAsync(View);
            _time.Now += TimeSpan.FromSeconds(5);
            var second = await source.GetAsync(View);

            Assert.Null(first.Quote);
            Assert.Same(first, second);
            ClientWasAsked(1);
        }

        [Fact]
        public async Task AfterAFailure_TheNextReadingReplacesIt()
        {
            var answers = new Queue<MisIndexQuote?>([null, Quote]);
            _client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => answers.Dequeue());
            var source = CreateSource();

            await source.GetAsync(Refresh);
            _time.Now += Refresh;
            var recovered = await source.GetAsync(Refresh);

            Assert.Same(Quote, recovered.Quote);
        }

        [Fact]
        public async Task ManyAtOnce_ShareOneRequest()
        {
            var release = new TaskCompletionSource<MisIndexQuote?>();
            var asked = 0;
            _client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
                .Returns(() => { Interlocked.Increment(ref asked); return release.Task; });
            var source = CreateSource();

            var calls = Enumerable.Range(0, 20).Select(i => Task.Run(() => source.GetAsync(i % 2 == 0 ? View : Refresh))).ToArray();
            await Task.Delay(100);
            release.SetResult(Quote);
            var snapshots = await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(1, asked);
            Assert.All(snapshots, s => Assert.Same(snapshots[0], s));
        }

        [Fact]
        public async Task ACallerThatGivesUpWhileWaiting_DoesNotAffectTheOthers()
        {
            var release = new TaskCompletionSource<MisIndexQuote?>();
            _client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).Returns(release.Task);
            var source = CreateSource();
            using var cts = new CancellationTokenSource();

            var first = source.GetAsync(View);                  // on its way to MIS
            await Task.Delay(50);
            var impatient = source.GetAsync(View, cts.Token);   // waiting behind it
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => impatient);
            release.SetResult(Quote);

            Assert.Same(Quote, (await first.WaitAsync(TimeSpan.FromSeconds(10))).Quote);
            ClientWasAsked(1);
        }

        [Fact]
        public async Task ACallerThatGivesUpMidRequest_LeavesNothingHeld_SoTheNextOneAsksAgain()
        {
            using var cts = new CancellationTokenSource();
            _client.SetupSequence(c => c.GetAsync(It.IsAny<CancellationToken>()))
                .Returns(async () => { await cts.CancelAsync(); throw new OperationCanceledException(cts.Token); })
                .ReturnsAsync(Quote);
            var source = CreateSource();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetAsync(View, cts.Token));
            var next = await source.GetAsync(View);

            Assert.Same(Quote, next.Quote);
            ClientWasAsked(2);
        }

        private sealed class FakeTime : TimeProvider
        {
            public DateTimeOffset Now { get; set; }
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
