using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Chatbot;
using SQL = JackyAIApp.Server.Data.Models.SQL;

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
            Assert.Equal(ChatErrorCodes.Refused, AnthropicChatService.NoAnswerCode(BetaStopReason.Refusal));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("max_tokens")]
        [InlineData("end_turn")]
        public void AnyOtherEmptyTurn_IsANoAnswer(string? stopReason)
        {
            BetaStopReason? reason = stopReason switch
            {
                "max_tokens" => BetaStopReason.MaxTokens,
                "end_turn" => BetaStopReason.EndTurn,
                _ => null,
            };

            Assert.Equal(ChatErrorCodes.NoAnswer, AnthropicChatService.NoAnswerCode(reason));
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

        [Fact]
        public void CachedInput_IsCostedAtItsOwnRates()
        {
            // 50K in all: 10K uncached, 10K written to the cache (x1.25), 30K read from it (x0.1),
            // at $0.10 per MTok; no output.
            var cost = AiRates.ChatCost("claude-haiku-5-5", 50_000, 0, cacheWriteTokens: 10_000, cacheReadTokens: 30_000);
            Assert.Equal((10_000 + 12_500 + 3_000) / 1_000_000m * 0.10m, cost);
        }

        [Fact]
        public void TheLongPromptRateCard_IsChosenOnTheWholePrompt_CacheIncluded()
        {
            // Only 10K uncached, but 190K read from the cache: a 200K prompt, on the $0.50 card.
            var cost = AiRates.ChatCost("claude-haiku-5-5", 200_000, 0, cacheReadTokens: 190_000);
            Assert.Equal((10_000 + 19_000) / 1_000_000m * 0.50m, cost);
        }

        [Fact]
        public void EveryTurn_AsksForTheConversationToBeCached()
        {
            var json = Body(AnthropicChatService.BuildRequest(new DateOnly(2026, 10, 21), []));

            Assert.Equal("ephemeral", json.GetProperty("cache_control").GetProperty("type").GetString());
        }

        [Fact]
        public void TheSameStoredConversation_IsSentAsTheSameBytes_SoTheNextTurnReadsItFromTheCache()
        {
            SQL.ChatMessage[] History()
            {
                var user = new SQL.ChatMessage { ConversationId = "c", Role = "user", Content = "看這張" };
                user.Attachments.Add(new SQL.ChatAttachment
                {
                    Id = 1, UserId = "u", FileName = "圖.png", Kind = AttachmentKinds.Image, Text = "",
                    BlobName = "b", MediaType = "image/png",
                });
                user.Attachments.Add(new SQL.ChatAttachment
                {
                    Id = 2, UserId = "u", FileName = "表.csv", Kind = AttachmentKinds.Csv, Text = "a,b",
                });
                return [user, new SQL.ChatMessage { ConversationId = "c", Role = "assistant", Content = "好的" }];
            }
            var files = new Dictionary<long, byte[]> { [1] = [1, 2, 3] };
            string Render() => Body(AnthropicChatService.BuildRequest(
                new DateOnly(2026, 10, 21), History().Select(m => AnthropicChatService.ToMessageParam(m, files)).ToList()))
                .GetRawText();

            var first = Render();
            Assert.Contains("\"messages\":[{", first);
            Assert.Equal(first, Render());
        }

        /// <summary>The request body as it goes over the wire.</summary>
        private static JsonElement Body(MessageCreateParams request) =>
            JsonSerializer.SerializeToElement(request).GetProperty("RawBodyData");
    }
}
