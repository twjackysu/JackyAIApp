using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Helpers;
using Anthropic.Models.Beta.Messages;
using AnthropicModel = Anthropic.Models.Messages.Model;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>
    /// Proves, against the real SDK, the round trip a tool the server runs itself needs (the shared
    /// AI chat's agent loop, docs/ai-native/design/04): a streamed reply that thinks and then calls a
    /// client tool is collected whole by the SDK's <see cref="BetaMessageContentAggregator"/> while its
    /// events still stream past, and the next request sends that reply back unchanged — the thinking
    /// block with its signature, which the API requires back on a tool continuation — followed by the
    /// tool's result.
    /// </summary>
    public class ClientToolRoundTripTests
    {
        [Fact]
        public async Task AThinkingToolCall_IsSentBackWithItsSignature_ThenItsResult()
        {
            var api = new FakeApi();
            api.Reply(
                Event("message_start", new
                {
                    type = "message_start",
                    message = new
                    {
                        id = "msg_1", type = "message", role = "assistant", model = "claude-haiku-5-5",
                        content = Array.Empty<object>(), stop_reason = (string?)null, stop_sequence = (string?)null,
                        usage = new { input_tokens = 10, output_tokens = 1 },
                    },
                }),
                Event("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "thinking", thinking = "", signature = "" } }),
                Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "thinking_delta", thinking = "要查單字。" } }),
                Event("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "signature_delta", signature = "sig-abc" } }),
                Event("content_block_stop", new { type = "content_block_stop", index = 0 }),
                Event("content_block_start", new { type = "content_block_start", index = 1, content_block = new { type = "tool_use", id = "toolu_1", name = "dictionary_lookup", input = new { } } }),
                Event("content_block_delta", new { type = "content_block_delta", index = 1, delta = new { type = "input_json_delta", partial_json = "{\"word\":" } }),
                Event("content_block_delta", new { type = "content_block_delta", index = 1, delta = new { type = "input_json_delta", partial_json = "\"serendipity\"}" } }),
                Event("content_block_stop", new { type = "content_block_stop", index = 1 }),
                Event("message_delta", new { type = "message_delta", delta = new { stop_reason = "tool_use", stop_sequence = (string?)null }, usage = new { output_tokens = 20 } }),
                Event("message_stop", new { type = "message_stop" }));
            var client = new AnthropicClient
            {
                ApiKey = "test-key", HttpClient = new HttpClient(api), BaseUrl = "https://anthropic.test", MaxRetries = 0,
            };
            var tool = new BetaTool
            {
                Name = "dictionary_lookup",
                Description = "查英文單字",
                InputSchema = new InputSchema
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["word"] = JsonSerializer.SerializeToElement(new { type = "string" }),
                    },
                    Required = ["word"],
                },
            };
            var messages = new List<BetaMessageParam> { new() { Role = Role.User, Content = "查 serendipity" } };
            MessageCreateParams Request() => new()
            {
                Model = AnthropicModel.ClaudeHaiku5_5,
                MaxTokens = 1000,
                Tools = [tool],
                Messages = [.. messages],
            };

            // The events still reach the reader one by one while the aggregator keeps them.
            var aggregator = new BetaMessageContentAggregator();
            var seen = 0;
            await foreach (var _ in aggregator.CollectAsync(client.Beta.Messages.CreateStreaming(Request()))) seen++;
            var reply = aggregator.Message();

            Assert.Equal(11, seen);
            Assert.Equal(BetaStopReason.ToolUse, reply.StopReason?.Value());
            var calls = new List<BetaToolUseBlock>();
            foreach (var block in reply.Content)
                if (block.TryPickToolUse(out var use)) calls.Add(use);
            var call = Assert.Single(calls);
            Assert.Equal("toolu_1", call.ID);
            Assert.Equal("serendipity", call.Input["word"].GetString());

            // As the SDK's own tool runner does: the reply's blocks, through JSON, become the
            // assistant turn sent back.
            messages.Add(new BetaMessageParam
            {
                Role = Role.Assistant,
                Content = new BetaMessageParamContent(JsonSerializer.SerializeToElement(reply.Content.Select(b => b.Json).ToArray())),
            });
            messages.Add(new BetaMessageParam
            {
                Role = Role.User,
                Content = new List<BetaContentBlockParam>
                {
                    new BetaToolResultBlockParam { ToolUseID = call.ID, Content = "serendipity：意外發現珍奇事物的運氣" },
                },
            });
            var sent = JsonSerializer.SerializeToElement(Request()).GetProperty("RawBodyData").GetProperty("messages");

            Assert.Equal(3, sent.GetArrayLength());
            var assistant = sent[1].GetProperty("content");
            Assert.Equal("thinking", assistant[0].GetProperty("type").GetString());
            Assert.Equal("要查單字。", assistant[0].GetProperty("thinking").GetString());
            Assert.Equal("sig-abc", assistant[0].GetProperty("signature").GetString());
            Assert.Equal("tool_use", assistant[1].GetProperty("type").GetString());
            Assert.Equal("serendipity", assistant[1].GetProperty("input").GetProperty("word").GetString());
            var result = sent[2].GetProperty("content")[0];
            Assert.Equal("tool_result", result.GetProperty("type").GetString());
            Assert.Equal("toolu_1", result.GetProperty("tool_use_id").GetString());
        }

        private static string Event(string type, object data) => $"event: {type}\ndata: {JsonSerializer.Serialize(data)}\n\n";

        private sealed class FakeApi : HttpMessageHandler
        {
            private readonly Queue<string> _replies = new();

            public void Reply(params string[] events) => _replies.Enqueue(string.Concat(events));

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_replies.Dequeue(), Encoding.UTF8, "text/event-stream"),
                });
        }
    }
}
