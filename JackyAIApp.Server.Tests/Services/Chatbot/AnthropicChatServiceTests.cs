using Anthropic.Models.Beta.Messages;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Chatbot;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>
    /// The parts of the Finance AI assistant's request and reply handling that do not need the
    /// Anthropic API: what the model is told about the date, what the user is told when no answer
    /// came, and what a turn is costed at.
    /// </summary>
    public class AnthropicChatServiceTests
    {
        [Fact]
        public void TheSystemPrompt_StartsWithTodaysDateInTaipei()
        {
            var prompt = AnthropicChatService.SystemPromptFor(new DateOnly(2026, 10, 21));

            Assert.StartsWith("今天是 2026-10-21（台北時間）。", prompt);
            Assert.Contains("tw-stock", prompt);
        }

        [Fact]
        public void ADecline_IsNotReportedAsRunningOutOfRoom()
        {
            // "Try again" would only get the same decline.
            var declined = AnthropicChatService.NoAnswerMessage(BetaStopReason.Refusal);

            Assert.Contains("無法回答", declined);
            Assert.DoesNotContain("再試一次", declined);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("max_tokens")]
        [InlineData("end_turn")]
        public void AnyOtherEmptyTurn_SuggestsTryingAgain(string? stopReason)
        {
            BetaStopReason? reason = stopReason switch
            {
                "max_tokens" => BetaStopReason.MaxTokens,
                "end_turn" => BetaStopReason.EndTurn,
                _ => null,
            };

            Assert.Contains("再試一次", AnthropicChatService.NoAnswerMessage(reason));
        }

        [Fact]
        public void Haiku55_IsCostedOnItsShortPromptRateCard()
        {
            // 50K in, 2K out at $0.10 / $0.50 per MTok.
            Assert.Equal(0.005m + 0.001m, AiRates.ChatCost("claude-haiku-5-5", 50_000, 2_000));
        }

        [Fact]
        public void Haiku55_PastAHundredThousandPromptTokens_IsCostedOnTheLongPromptRateCard()
        {
            // 200K in, 2K out at $0.50 / $2.50 per MTok.
            Assert.Equal(0.1m + 0.005m, AiRates.ChatCost("claude-haiku-5-5", 200_000, 2_000));
            // Exactly at the threshold is still the short-prompt card.
            Assert.Equal(0.01m, AiRates.ChatCost("claude-haiku-5-5", 100_000, 0));
        }

        [Fact]
        public void Haiku45_KeepsItsRate_ForTheRowsLoggedBeforeTheSwitch()
        {
            Assert.Equal(1.00m + 5.00m, AiRates.ChatCost("claude-haiku-4-5", 1_000_000, 1_000_000));
        }
    }
}
