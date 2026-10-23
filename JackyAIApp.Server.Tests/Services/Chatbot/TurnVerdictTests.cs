using Anthropic.Models.Beta.Messages;
using JackyAIApp.Server.Services.Chatbot;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>What a turn's outcome comes to: an answer, a failure, or a stop, and what of it is kept.</summary>
    public class TurnVerdictTests
    {
        private static TurnOutcome Outcome(
            string text = "台積電今天上漲。", BetaStopReason? stopReason = BetaStopReason.EndTurn, bool stopped = false,
            string? errorCode = null) =>
            new(text, text.Length > 0, stopReason, stopped, errorCode, default, [], 0);

        [Fact]
        public void AnAnswer_Succeeds_AndIsKept()
        {
            Assert.Equal(new TurnVerdict(true, null, false, false, "台積電今天上漲。"), TurnVerdict.Of(Outcome()));
        }

        [Fact]
        public void AnAnswerCutAtTheOutputCap_IsKept_AndMarkedTruncated()
        {
            Assert.Equal(new TurnVerdict(true, null, true, false, "台積電今天上漲。"),
                TurnVerdict.Of(Outcome(stopReason: BetaStopReason.MaxTokens)));
        }

        [Theory]
        [InlineData("說到一半")]
        [InlineData("")]
        public void AStoppedTurn_KeepsWhatWasSaid_MarkedCutOff(string text)
        {
            Assert.Equal(new TurnVerdict(false, null, false, true, AnthropicChatService.StoppedContent(text)),
                TurnVerdict.Of(Outcome(text, stopReason: null, stopped: true)));
        }

        [Fact]
        public void AFailedRequest_KeepsTheTextItSent_AsItIs()
        {
            Assert.Equal(new TurnVerdict(false, ChatErrorCodes.Busy, false, false, "說到一半"),
                TurnVerdict.Of(Outcome("說到一半", stopReason: null, errorCode: ChatErrorCodes.Busy)));
        }

        [Fact]
        public void AFailedRequestWithNoText_KeepsNothing()
        {
            Assert.Equal(new TurnVerdict(false, ChatErrorCodes.ContextTooLong, false, false, null),
                TurnVerdict.Of(Outcome("", stopReason: null, errorCode: ChatErrorCodes.ContextTooLong)));
        }

        [Theory]
        [InlineData(BetaStopReason.EndTurn, ChatErrorCodes.NoAnswer)]
        [InlineData(BetaStopReason.MaxTokens, ChatErrorCodes.NoAnswer)]
        [InlineData(BetaStopReason.PauseTurn, ChatErrorCodes.NoAnswer)]
        [InlineData(BetaStopReason.Refusal, ChatErrorCodes.Refused)]
        public void NoAnswerText_IsAFailure_ByWhyItStopped(BetaStopReason stopReason, string code)
        {
            Assert.Equal(new TurnVerdict(false, code, false, false, null), TurnVerdict.Of(Outcome("", stopReason)));
        }

        [Fact]
        public void ADeclineAfterSomeText_IsRefused_AndTheCutOffAnswerIsNotKept()
        {
            Assert.Equal(new TurnVerdict(false, ChatErrorCodes.Refused, false, false, null),
                TurnVerdict.Of(Outcome("我可以告訴你", BetaStopReason.Refusal)));
        }
    }
}
