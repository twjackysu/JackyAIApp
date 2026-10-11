using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    public class TaiexDataStatusTests
    {
        [Fact]
        public void AtStart_NothingHasBeenChecked()
        {
            var status = new TaiexDataStatus();

            Assert.Null(status.LastCheckedAt);
            Assert.Equal(0, status.Version);
        }

        [Fact]
        public void RecordCheck_SetsTheTime_InUtc_AndMovesTheVersion()
        {
            var status = new TaiexDataStatus();
            var at = new DateTimeOffset(2026, 10, 8, 15, 5, 0, TimeSpan.FromHours(8));

            status.RecordCheck(at);

            Assert.Equal<DateTimeOffset?>(at, status.LastCheckedAt);
            Assert.Equal(TimeSpan.Zero, status.LastCheckedAt!.Value.Offset);
            Assert.Equal(1, status.Version);
        }

        [Fact]
        public void EveryCheck_MovesTheVersion_EvenAtTheSameTime()
        {
            var status = new TaiexDataStatus();
            var at = new DateTimeOffset(2026, 10, 8, 7, 0, 0, TimeSpan.Zero);

            status.RecordCheck(at);
            var first = status.Version;
            status.RecordCheck(at);

            Assert.NotEqual(first, status.Version);
        }

        [Fact]
        public async Task ConcurrentChecks_AreAllCounted()
        {
            var status = new TaiexDataStatus();
            var at = new DateTimeOffset(2026, 10, 8, 7, 0, 0, TimeSpan.Zero);

            await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => status.RecordCheck(at))));

            Assert.Equal(200, status.Version);
        }
    }
}
