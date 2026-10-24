using JackyAIApp.Server.Services.Chatbot;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>
    /// Who can answer a question a turn is waiting on, and what an answer comes to (the whole wait, in
    /// its stream, is in <see cref="AnthropicChatStreamTests"/>).
    /// </summary>
    public class ChatApprovalRegistryTests
    {
        private readonly ChatApprovalRegistry _registry = new();

        [Fact]
        public async Task TheReaderAsked_CanAnswer_AndACallTheyLeftOutIsANo()
        {
            using var waiting = _registry.Begin("u1", "c1", ["a", "b"]);

            Assert.True(_registry.Resolve("u1", "c1", waiting.PendingId, [new ChatApprovalDecision("a", true)]));

            var decided = await waiting.Decided;
            Assert.True(decided["a"]);
            Assert.False(decided["b"]);
        }

        [Fact]
        public async Task ADecisionOnACallNotAsked_IsIgnored()
        {
            using var waiting = _registry.Begin("u1", "c1", ["a"]);

            Assert.True(_registry.Resolve("u1", "c1", waiting.PendingId,
                [new ChatApprovalDecision("a", false), new ChatApprovalDecision("other", true)]));

            var decided = Assert.Single(await waiting.Decided);
            Assert.Equal("a", decided.Key);
            Assert.False(decided.Value);
        }

        [Theory]
        [InlineData("u2", "c1")]
        [InlineData("u1", "c2")]
        public void SomeoneElse_OrAnotherConversation_CannotAnswer(string userId, string conversationId)
        {
            using var waiting = _registry.Begin("u1", "c1", ["a"]);

            Assert.False(_registry.Resolve(userId, conversationId, waiting.PendingId, [new ChatApprovalDecision("a", true)]));
            Assert.False(waiting.Decided.IsCompleted);
        }

        [Fact]
        public void AQuestion_IsAnsweredOnce()
        {
            using var waiting = _registry.Begin("u1", "c1", ["a"]);

            Assert.True(_registry.Resolve("u1", "c1", waiting.PendingId, [new ChatApprovalDecision("a", true)]));
            Assert.False(_registry.Resolve("u1", "c1", waiting.PendingId, [new ChatApprovalDecision("a", false)]));
        }

        [Fact]
        public void OnceTheTurnStopsWaiting_AnAnswerFindsNothing()
        {
            var waiting = _registry.Begin("u1", "c1", ["a"]);
            waiting.Dispose();

            Assert.False(_registry.Resolve("u1", "c1", waiting.PendingId, [new ChatApprovalDecision("a", true)]));
            Assert.True(waiting.Decided.IsCanceled);
        }

        [Fact]
        public void EachQuestion_HasItsOwnId()
        {
            using var first = _registry.Begin("u1", "c1", ["a"]);
            using var second = _registry.Begin("u1", "c1", ["a"]);

            Assert.NotEqual(first.PendingId, second.PendingId);
        }
    }
}
