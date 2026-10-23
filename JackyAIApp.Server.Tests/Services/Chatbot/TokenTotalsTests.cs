using JackyAIApp.Server.Services.Chatbot;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>A turn's tokens: each request's latest report, summed over the turn's requests.</summary>
    public class TokenTotalsTests
    {
        [Fact]
        public void ARequestsUsage_IsItsLatestReport_AndAMissingFigureKeepsTheLastOne()
        {
            var usage = new RequestUsage();
            usage.Update(input: 100, cacheWrite: 20, cacheRead: 30, output: 1);   // message_start
            usage.Update(input: null, cacheWrite: null, cacheRead: null, output: 50);   // final message_delta

            Assert.Equal((100L, 20L, 30L, 50L), (usage.Input, usage.CacheWrite, usage.CacheRead, usage.Output));
        }

        [Fact]
        public void InputCountsEverything_UncachedWrittenToAndReadFromTheCache()
        {
            var usage = new RequestUsage();
            usage.Update(input: 100, cacheWrite: 20, cacheRead: 30, output: 50);

            Assert.Equal(new TokenTotals(Input: 150, Output: 50, CacheWrite: 20, CacheRead: 30), new TokenTotals().Plus(usage));
        }

        [Fact]
        public void EveryRequestOfTheTurn_Adds()
        {
            var paused = new RequestUsage();
            paused.Update(input: 100, cacheWrite: 0, cacheRead: 0, output: 10);
            var answered = new RequestUsage();
            answered.Update(input: 10, cacheWrite: 5, cacheRead: 100, output: 40);

            Assert.Equal(new TokenTotals(Input: 215, Output: 50, CacheWrite: 5, CacheRead: 100),
                new TokenTotals().Plus(paused).Plus(answered));
        }
    }
}
