using JackyAIApp.Server.Services.Chatbot;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>Stopping the answer being written, and what is kept of it.</summary>
    public class ChatTurnRegistryTests
    {
        [Fact]
        public void Stop_CancelsTheTurnBeingAnswered()
        {
            var turns = new ChatTurnRegistry();
            using var turn = turns.Begin("u1", "c1");

            Assert.True(turns.Stop("u1", "c1"));
            Assert.True(turn.Token.IsCancellationRequested);
        }

        [Fact]
        public void AnotherUser_CannotStopTheTurn_EvenKnowingTheConversation()
        {
            var turns = new ChatTurnRegistry();
            using var turn = turns.Begin("u1", "c1");

            Assert.False(turns.Stop("u2", "c1"));
            Assert.False(turn.Token.IsCancellationRequested);
        }

        [Fact]
        public void AFinishedTurn_HasNothingToStop()
        {
            var turns = new ChatTurnRegistry();
            turns.Begin("u1", "c1").Dispose();

            Assert.False(turns.Stop("u1", "c1"));
        }

        [Fact]
        public void ANewTurnInTheSameConversation_StopsTheOneBefore_AndTheOldOneEndingLeavesTheNewOne()
        {
            var turns = new ChatTurnRegistry();
            var first = turns.Begin("u1", "c1");
            using var second = turns.Begin("u1", "c1");
            Assert.True(first.Token.IsCancellationRequested);

            // The first turn finishing must not unregister the second.
            first.Dispose();
            Assert.True(turns.Stop("u1", "c1"));
            Assert.True(second.Token.IsCancellationRequested);
        }

        [Fact]
        public async Task Settle_StopsTheRunningTurn_AndWaitsUntilItHasSaved()
        {
            var turns = new ChatTurnRegistry();
            var running = turns.Begin("u1", "c1");

            var settle = turns.SettleAsync("u1", "c1", TimeSpan.FromSeconds(30), CancellationToken.None);

            Assert.True(running.Token.IsCancellationRequested);
            // The stopped turn is still saving its answer: the new message must not be stored yet.
            await Task.Delay(50);
            Assert.False(settle.IsCompleted);

            running.Dispose();
            await settle.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task Settle_WithNoTurnRunning_GoesOnAtOnce()
        {
            var turns = new ChatTurnRegistry();
            turns.Begin("u1", "c1").Dispose();

            await turns.SettleAsync("u1", "c1", TimeSpan.FromSeconds(30), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            // Another user's turn on a conversation of the same id is neither stopped nor waited for.
            using var other = turns.Begin("u2", "c1");
            await turns.SettleAsync("u1", "c1", TimeSpan.FromSeconds(30), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(other.Token.IsCancellationRequested);
        }

        [Fact]
        public async Task Settle_DoesNotWaitForeverForATurnThatDoesNotEnd()
        {
            var turns = new ChatTurnRegistry();
            using var stuck = turns.Begin("u1", "c1");

            await turns.SettleAsync("u1", "c1", TimeSpan.FromMilliseconds(50), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(stuck.Token.IsCancellationRequested);
        }

        [Theory]
        [InlineData("台積電目前", "台積電目前\n\n（已中斷）")]
        [InlineData("台積電目前\n", "台積電目前\n\n（已中斷）")]
        [InlineData("", "（已中斷）")]
        [InlineData("  ", "（已中斷）")]
        public void AStoppedAnswer_IsKeptWithAMarkSayingSo(string partial, string stored) =>
            Assert.Equal(stored, AnthropicChatService.StoppedContent(partial));
    }
}
