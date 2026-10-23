using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Assistant;
using JackyAIApp.Server.Services.Chatbot;
using JackyAIApp.Server.Services.Finance.Assistant;
using JackyAIApp.Server.Tests.TestHelpers;
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
            var prompt = FinanceChatProfile.SystemPromptFor(new DateOnly(2026, 10, 21));

            Assert.StartsWith("今天是 2026-10-21（台北時間）。", prompt);
            Assert.Contains("tw-stock", prompt);
        }

        [Fact]
        public void TheSystemPrompt_SaysHowToFindADeferredTool_AndToWriteEverythingInChinese()
        {
            var prompt = FinanceChatProfile.SystemPromptFor(new DateOnly(2026, 10, 21));

            Assert.Contains("tool_search_tool_regex", prompt);
            Assert.Contains("不必再搜尋", prompt);
            Assert.Contains("包括呼叫工具之間的說明，都用繁體中文", prompt);
        }

        [Fact]
        public void TheToolsAreFoundWithARegexSearch_ExceptTheAlwaysLoadedOnes()
        {
            var tools = Body(FinanceChatRequest.Build(new DateOnly(2026, 10, 21), []))
                .GetProperty("tools").EnumerateArray().ToList();

            Assert.Contains(tools, t => t.GetProperty("type").GetString() == "tool_search_tool_regex_20251119");
            // BM25 found almost no tool for questions asked in Chinese.
            Assert.DoesNotContain(tools, t => t.GetProperty("type").GetString()!.StartsWith("tool_search_tool_bm25"));

            var toolset = Assert.Single(tools, t => t.GetProperty("type").GetString() == "mcp_toolset");
            Assert.Equal("tw-stock", toolset.GetProperty("mcp_server_name").GetString());
            Assert.True(toolset.GetProperty("default_config").GetProperty("defer_loading").GetBoolean());

            var configs = toolset.GetProperty("configs");
            Assert.Equal(ChatTurnRequest.AlwaysLoadedTools.Length, configs.EnumerateObject().Count());
            foreach (var name in ChatTurnRequest.AlwaysLoadedTools)
                Assert.False(configs.GetProperty(name).GetProperty("defer_loading").GetBoolean());

            // A deferred tool with a cache breakpoint is a 400; the toolset defers by default.
            Assert.False(toolset.TryGetProperty("cache_control", out _));
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
            var json = Body(FinanceChatRequest.Build(new DateOnly(2026, 10, 21), []));

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
            string Render() => Body(FinanceChatRequest.Build(
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
