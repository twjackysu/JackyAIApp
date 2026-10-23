using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.AiUsage;
using JackyAIApp.Server.Services.Chatbot;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>
    /// One chat turn end to end: the real Anthropic SDK reads recorded API streams served by a fake
    /// HttpClient, and what the reader gets (the SSE events), what is saved and what is logged are checked.
    /// </summary>
    public class AnthropicChatStreamTests : IDisposable
    {
        private const string User = "chat-user";

        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly FakeAnthropic _anthropic = new();
        private readonly Mock<IChatAttachmentStore> _attachments = new();
        private readonly Mock<IAnthropicFileStore> _files = new();
        private readonly ChatTurnRegistry _turns = new();
        private readonly Mock<IMarketBackground> _background = new();
        private readonly Mock<IAiUsageLogger> _usage = new();
        private readonly UsageLedger _ledger = new();
        private readonly RecordingLogger _log = new();
        private string? _apiKey = "test-key";

        public void Dispose() => _db.Dispose();

        private AnthropicChatService Service()
        {
            var settings = new Mock<IOptionsMonitor<Settings>>();
            settings.Setup(s => s.CurrentValue).Returns(() => new Settings { Google = new JackyAIApp.Server.Configuration.Google(), AnthropicApiKey = _apiKey });
            return new AnthropicChatService(_db, _attachments.Object, _files.Object, _turns, _background.Object, _usage.Object,
                _ledger, settings.Object, new TestClientFactory(_anthropic), _log);
        }

        /// <summary>Keeps each log entry's named values (the message template's placeholders).</summary>
        private sealed class RecordingLogger : ILogger<AnthropicChatService>
        {
            public List<IReadOnlyList<KeyValuePair<string, object?>>> Entries { get; } = [];

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (state is IReadOnlyList<KeyValuePair<string, object?>> values) Entries.Add(values);
            }
        }

        private sealed class TestClientFactory(HttpMessageHandler handler) : IAnthropicClientFactory
        {
            // No SDK retries: a test answers each request itself.
            public AnthropicClient Create(string apiKey) =>
                new() { ApiKey = apiKey, HttpClient = new HttpClient(handler), BaseUrl = "https://anthropic.test", MaxRetries = 0 };
        }

        private sealed record Turn(ChatStreamResult Result, List<JsonElement> Events);

        private async Task<Turn> AskAsync(
            string message = "分析一下2330", string? conversationId = null, SQL.ChatAttachment[]? attachments = null,
            CancellationToken ct = default, RecordingBody? body = null)
        {
            var context = new DefaultHttpContext();
            body ??= new RecordingBody();
            context.Response.Body = body;
            var result = await Service().StreamChatAsync(User, message, conversationId, attachments ?? [], context.Response, ct);
            return new Turn(result, body.Events());
        }

        private static string Type(JsonElement e) => e.GetProperty("type").GetString()!;

        private async Task<SQL.ChatConversation> ConversationAsync()
        {
            var conversation = new SQL.ChatConversation { UserId = User };
            _db.ChatConversations.Add(conversation);
            await _db.SaveChangesAsync();
            return conversation;
        }

        private List<SQL.ChatMessage> Messages() => _db.ChatMessages.AsNoTracking().OrderBy(m => m.Id).ToList();

        // ── answers ─────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task AnAnswer_StreamsToTheReader_IsSaved_AndIsLoggedWithItsTokens()
        {
            _anthropic.Reply(Sse.Start(input: 100, cacheWrite: 20, cacheRead: 30), Sse.ToolUse("get_realtime_quote"), Sse.ToolSearch(),
                Sse.Text("台積電"), Sse.Text("今天上漲。"), Sse.End("end_turn", output: 50));

            var turn = await AskAsync();

            Assert.Equal(new[] { "start", "tool_use", "delta", "delta", "done" }, turn.Events.Select(Type));
            Assert.Equal("get_realtime_quote", turn.Events[1].GetProperty("name").GetString());
            var done = turn.Events[^1];
            Assert.False(done.GetProperty("truncated").GetBoolean());
            Assert.Equal(new ChatStreamResult(true, done.GetProperty("conversationId").GetString()!, null), turn.Result);

            var messages = Messages();
            Assert.Equal(new[] { "user", "assistant" }, messages.Select(m => m.Role));
            Assert.Equal("台積電今天上漲。", messages[1].Content);
            Assert.Equal(messages[1].Id, done.GetProperty("assistantMessageId").GetInt64());
            Assert.Equal("分析一下2330", _db.ChatConversations.Single().Title);
            // Input is all of it: uncached, written to and read from the cache.
            _usage.Verify(u => u.LogChatAsync("chatbot.tw_stock", "claude-haiku-5-5", 150, 50, true, User, 20, 30), Times.Once);
        }

        [Fact]
        public async Task OnceAnthropicStartsTheMessage_ItIsBilled_SoTheChargeIsNotRefunded()
        {
            _ledger.RecordCharge(User, UsageFeature.ChatMessage, 1);
            _anthropic.Reply(Sse.Start(), Sse.End("end_turn"));   // no answer text: a failed turn

            await AskAsync();

            Assert.Empty(_ledger.TakeRefunds(responseFailed: true));
        }

        [Fact]
        public async Task AnAnswerCutAtTheOutputCap_IsKept_AndMarkedTruncated()
        {
            _anthropic.Reply(Sse.Start(), Sse.Text("說到一半"), Sse.End("max_tokens"));

            var turn = await AskAsync();

            Assert.True(turn.Events[^1].GetProperty("truncated").GetBoolean());
            Assert.Equal("說到一半", Messages()[1].Content);
        }

        [Fact]
        public async Task TheMarketBackground_GoesAsASecondSystemBlock()
        {
            _background.Setup(b => b.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync("加權指數 23,000");
            _anthropic.Reply(Sse.Start(), Sse.Text("好"), Sse.End("end_turn"));

            await AskAsync();

            var system = JsonDocument.Parse(Assert.Single(_anthropic.Requests)).RootElement.GetProperty("system");
            Assert.Equal(2, system.GetArrayLength());
            Assert.Contains("加權指數 23,000", system[1].GetProperty("text").GetString());
        }

        [Fact]
        public async Task WithoutTheMarketBackground_TheTurnStillAnswers()
        {
            _background.Setup(b => b.GetAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("TWSE down"));
            _anthropic.Reply(Sse.Start(), Sse.Text("好"), Sse.End("end_turn"));

            var turn = await AskAsync();

            Assert.Equal("done", Type(turn.Events[^1]));
            var system = JsonDocument.Parse(Assert.Single(_anthropic.Requests)).RootElement.GetProperty("system");
            Assert.Equal(JsonValueKind.String, system.ValueKind);
        }

        // ── retries ─────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task APauseTurnWithNoText_IsRetried_AndTheTokensOfEveryAttemptCount()
        {
            _anthropic.Reply(Sse.Start(input: 100), Sse.End("pause_turn", output: 10));
            _anthropic.Reply(Sse.Start(input: 120), Sse.Text("答案"), Sse.End("end_turn", output: 40));

            var turn = await AskAsync();

            Assert.Equal("done", Type(turn.Events[^1]));
            Assert.Equal(2, _anthropic.Requests.Count);
            _usage.Verify(u => u.LogChatAsync(It.IsAny<string>(), It.IsAny<string>(), 220, 50, true, User, 0, 0), Times.Once);
        }

        [Fact]
        public async Task PauseTurnEveryTime_GivesUpAfterTwoRetries_AsNoAnswer()
        {
            for (var i = 0; i < 3; i++) _anthropic.Reply(Sse.Start(), Sse.End("pause_turn"));

            var turn = await AskAsync();

            Assert.Equal(3, _anthropic.Requests.Count);
            Assert.Equal(ChatErrorCodes.NoAnswer, turn.Events[^1].GetProperty("code").GetString());
            Assert.Equal(new[] { "user" }, Messages().Select(m => m.Role));
        }

        [Fact]
        public async Task AFileTheFilesApiNoLongerHas_IsUploadedAgain_AndTheTurnRetried()
        {
            var image = new SQL.ChatAttachment
            {
                UserId = User, FileName = "chart.png", Kind = AttachmentKinds.Image, Text = "",
                BlobName = "blob-1", MediaType = "image/png", AnthropicFileId = "file_old",
            };
            _db.ChatAttachments.Add(image);
            await _db.SaveChangesAsync();
            _attachments.Setup(a => a.ReadAsync("blob-1", It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
            _files.Setup(f => f.UploadAsync(It.IsAny<byte[]>(), "image/png", It.IsAny<CancellationToken>())).ReturnsAsync("file_new");
            _anthropic.Fail(HttpStatusCode.NotFound, "not_found_error", "File not found: file_old");
            _anthropic.Reply(Sse.Start(), Sse.Text("看到了"), Sse.End("end_turn"));

            var turn = await AskAsync(attachments: [image]);

            Assert.Equal("done", Type(turn.Events[^1]));
            Assert.Contains("file_old", _anthropic.Requests[0]);
            Assert.Contains("file_new", _anthropic.Requests[1]);
            Assert.Equal("file_new", _db.ChatAttachments.AsNoTracking().Single().AnthropicFileId);
            _files.Verify(f => f.DeleteAsync(It.Is<IEnumerable<string>>(ids => ids.Single() == "file_old"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AFileStillMissingAfterUploadingAgain_IsNotUploadedAThirdTime()
        {
            var image = new SQL.ChatAttachment
            {
                UserId = User, FileName = "chart.png", Kind = AttachmentKinds.Image, Text = "",
                BlobName = "blob-1", MediaType = "image/png", AnthropicFileId = "file_old",
            };
            _db.ChatAttachments.Add(image);
            await _db.SaveChangesAsync();
            _attachments.Setup(a => a.ReadAsync("blob-1", It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
            _files.Setup(f => f.UploadAsync(It.IsAny<byte[]>(), "image/png", It.IsAny<CancellationToken>())).ReturnsAsync("file_new");
            _anthropic.Fail(HttpStatusCode.NotFound, "not_found_error", "File not found: file_old");
            _anthropic.Fail(HttpStatusCode.NotFound, "not_found_error", "File not found: file_new");

            var turn = await AskAsync(attachments: [image]);

            Assert.Equal(2, _anthropic.Requests.Count);
            Assert.Equal(ChatErrorCodes.Unavailable, turn.Events[^1].GetProperty("code").GetString());
            _files.Verify(f => f.UploadAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task APauseTurnAfterSomeText_IsNotRetried_SoTheReaderNeverGetsTheTextTwice()
        {
            _anthropic.Reply(Sse.Start(), Sse.Text("先查一下"), Sse.End("pause_turn"));

            var turn = await AskAsync();

            Assert.Single(_anthropic.Requests);
            Assert.Equal("done", Type(turn.Events[^1]));
            Assert.Equal("先查一下", Messages()[1].Content);
        }

        [Fact]
        public async Task ARetryThatEndsWithoutAStopReason_IsNotTakenForAnotherPause()
        {
            _anthropic.Reply(Sse.Start(), Sse.End("pause_turn"));
            _anthropic.Reply(Sse.Start());   // cut off before message_delta

            var turn = await AskAsync();

            Assert.Equal(2, _anthropic.Requests.Count);
            Assert.Equal(ChatErrorCodes.NoAnswer, turn.Events[^1].GetProperty("code").GetString());
        }

        [Fact]
        public async Task TheTurnLog_NamesTheToolsCalled_AndCountsTheToolSearches()
        {
            _anthropic.Reply(Sse.Start(), Sse.ToolSearch(), Sse.ToolUse("get_realtime_quote"), Sse.ToolSearch(),
                Sse.ToolUse("get_margin_balance"), Sse.Text("好"), Sse.End("end_turn"));

            await AskAsync();

            var entry = _log.Entries.Single(e => e.Any(kv => kv.Key == "ToolsCalled"));
            Assert.Equal("get_realtime_quote,get_margin_balance", entry.Single(kv => kv.Key == "ToolsCalled").Value);
            Assert.Equal(2, entry.Single(kv => kv.Key == "ToolSearches").Value);
        }

        // ── failures ────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(529, "overloaded_error", "Overloaded", ChatErrorCodes.Busy)]
        [InlineData(400, "invalid_request_error", "prompt is too long: 200001 tokens > 200000 maximum", ChatErrorCodes.ContextTooLong)]
        public async Task AnApiError_IsReportedByItsCode_AndNothingIsSaved(int status, string type, string message, string code)
        {
            _anthropic.Fail((HttpStatusCode)status, type, message);

            var turn = await AskAsync();

            Assert.Equal(new[] { "start", "error" }, turn.Events.Select(Type));
            Assert.Equal(code, turn.Events[^1].GetProperty("code").GetString());
            Assert.False(turn.Result.StreamingStarted);
            Assert.Equal(code, turn.Result.ErrorCode);
            Assert.Equal(new[] { "user" }, Messages().Select(m => m.Role));
            _usage.Verify(u => u.LogChatAsync(It.IsAny<string>(), It.IsAny<string>(), 0, 0, false, User, 0, 0), Times.Once);
        }

        [Theory]
        [InlineData("end_turn", ChatErrorCodes.NoAnswer)]
        [InlineData("refusal", ChatErrorCodes.Refused)]
        public async Task ATurnWithNoAnswerText_IsAFailure(string stopReason, string code)
        {
            _anthropic.Reply(Sse.Start(), Sse.End(stopReason));

            var turn = await AskAsync();

            Assert.Equal(code, turn.Events[^1].GetProperty("code").GetString());
            Assert.Equal(new[] { "user" }, Messages().Select(m => m.Role));
        }

        [Fact]
        public async Task ADeclineAfterSomeText_IsRefused_AndTheCutOffAnswerIsNotKept()
        {
            _anthropic.Reply(Sse.Start(), Sse.Text("我可以告訴你"), Sse.End("refusal"));

            var turn = await AskAsync();

            Assert.Equal(ChatErrorCodes.Refused, turn.Events[^1].GetProperty("code").GetString());
            Assert.True(turn.Result.StreamingStarted);
            Assert.Equal(new[] { "user" }, Messages().Select(m => m.Role));
        }

        [Fact]
        public async Task AnErrorAfterSomeText_IsReported_AndTheCutOffAnswerIsNotKept()
        {
            _anthropic.Reply(Sse.Start(), Sse.Text("說到一半"), Sse.Error("overloaded_error", "Overloaded"));

            var turn = await AskAsync();

            Assert.Equal(new[] { "start", "delta", "error" }, turn.Events.Select(Type));
            Assert.Equal(ChatErrorCodes.Busy, turn.Events[^1].GetProperty("code").GetString());
            Assert.Equal(new ChatStreamResult(true, Messages()[0].ConversationId, ChatErrorCodes.Busy), turn.Result);
            Assert.Equal(new[] { "user" }, Messages().Select(m => m.Role));
        }

        [Fact]
        public async Task WithNoApiKey_TheTurnIsUnavailable_AndAnthropicIsNotCalled()
        {
            _apiKey = null;

            var turn = await AskAsync(conversationId: "c-1");

            Assert.Equal(new[] { "error" }, turn.Events.Select(Type));
            Assert.Equal(new ChatStreamResult(false, "c-1", ChatErrorCodes.Unavailable), turn.Result);
            Assert.Empty(_anthropic.Requests);
        }

        // ── stopping ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task AStoppedTurn_KeepsWhatWasSaid_MarkedCutOff_AndTellsTheReader()
        {
            var conversation = await ConversationAsync();
            _anthropic.ReplyAndHang(Sse.Start(), Sse.Text("部分回答"));
            var body = new RecordingBody();

            var turn = AskAsync(conversationId: conversation.Id, body: body);
            await body.DeltaWritten.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(_turns.Stop(User, conversation.Id));
            var result = await turn;

            var done = result.Events[^1];
            Assert.Equal("done", Type(done));
            Assert.True(done.GetProperty("stopped").GetBoolean());
            Assert.Equal("部分回答\n\n" + AnthropicChatService.StoppedNote, Messages()[1].Content);
            _usage.Verify(u => u.LogChatAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), false, User,
                It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }

        [Fact]
        public async Task AReaderWhoLeft_StillGetsTheAnswerSoFarSaved_ButNothingMoreIsWritten()
        {
            var conversation = await ConversationAsync();
            _anthropic.ReplyAndHang(Sse.Start(), Sse.Text("部分回答"));
            var body = new RecordingBody();
            using var left = new CancellationTokenSource();

            var turn = AskAsync(conversationId: conversation.Id, ct: left.Token, body: body);
            await body.DeltaWritten.WaitAsync(TimeSpan.FromSeconds(10));
            left.Cancel();
            var result = await turn;

            Assert.Equal(new[] { "start", "delta" }, result.Events.Select(Type));
            Assert.Equal("部分回答\n\n" + AnthropicChatService.StoppedNote, Messages()[1].Content);
        }

        // ── the recorded API ────────────────────────────────────────────────────────────────────

        /// <summary>Anthropic's streaming events, as the Messages API sends them.</summary>
        private static class Sse
        {
            private static int _block;

            private static string Event(string type, object data) => $"event: {type}\ndata: {JsonSerializer.Serialize(data)}\n\n";

            public static string Start(int input = 10, int cacheWrite = 0, int cacheRead = 0) => Event("message_start", new
            {
                type = "message_start",
                message = new
                {
                    id = "msg_1", type = "message", role = "assistant", model = "claude-haiku-5-5", content = Array.Empty<object>(),
                    stop_reason = (string?)null, stop_sequence = (string?)null,
                    usage = new { input_tokens = input, output_tokens = 1, cache_creation_input_tokens = cacheWrite, cache_read_input_tokens = cacheRead },
                },
            });

            public static string Text(string text)
            {
                var index = _block++;
                return Event("content_block_start", new { type = "content_block_start", index, content_block = new { type = "text", text = "" } })
                    + Event("content_block_delta", new { type = "content_block_delta", index, delta = new { type = "text_delta", text } })
                    + Event("content_block_stop", new { type = "content_block_stop", index });
            }

            public static string ToolUse(string name)
            {
                var index = _block++;
                return Event("content_block_start", new
                {
                    type = "content_block_start", index,
                    content_block = new { type = "mcp_tool_use", id = "mcptoolu_1", name, server_name = "tw-stock", input = new { } },
                }) + Event("content_block_stop", new { type = "content_block_stop", index });
            }

            public static string ToolSearch()
            {
                var index = _block++;
                return Event("content_block_start", new
                {
                    type = "content_block_start", index,
                    content_block = new { type = "server_tool_use", id = "srvtoolu_1", name = "tool_search_tool_regex", input = new { pattern = "外資" } },
                }) + Event("content_block_stop", new { type = "content_block_stop", index });
            }

            public static string End(string stopReason, int output = 5) =>
                Event("message_delta", new
                {
                    type = "message_delta",
                    delta = new { stop_reason = stopReason, stop_sequence = (string?)null },
                    usage = new { output_tokens = output },
                }) + Event("message_stop", new { type = "message_stop" });

            /// <summary>An error the API reports after the stream has begun.</summary>
            public static string Error(string type, string message) =>
                Event("error", new { type = "error", error = new { type, message } });
        }

        /// <summary>Answers each request with the next recorded response, and keeps each request's body.</summary>
        private sealed class FakeAnthropic : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _responses = new();
            public List<string> Requests { get; } = [];

            public void Reply(params string[] events) =>
                _responses.Enqueue(() => Respond(new StringContent(string.Concat(events), Encoding.UTF8, "text/event-stream")));

            /// <summary>Sends these events, then nothing more until the request is cancelled.</summary>
            public void ReplyAndHang(params string[] events) =>
                _responses.Enqueue(() =>
                {
                    var content = new StreamContent(new HangingStream(Encoding.UTF8.GetBytes(string.Concat(events))));
                    content.Headers.ContentType = new("text/event-stream");
                    return Respond(content);
                });

            public void Fail(HttpStatusCode status, string type, string message) =>
                _responses.Enqueue(() => new HttpResponseMessage(status)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { type = "error", error = new { type, message } }), Encoding.UTF8, "application/json"),
                });

            private static HttpResponseMessage Respond(HttpContent content) => new(HttpStatusCode.OK) { Content = content };

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
                return _responses.Dequeue()();
            }
        }

        private sealed class HangingStream(byte[] prefix) : Stream
        {
            private int _position;

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                if (_position < prefix.Length)
                {
                    var n = Math.Min(buffer.Length, prefix.Length - _position);
                    prefix.AsMemory(_position, n).CopyTo(buffer);
                    _position += n;
                    return n;
                }
                await Task.Delay(Timeout.Infinite, ct);
                return 0;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
                ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>The response body: keeps what is written, and says when the first answer text went out.</summary>
        private sealed class RecordingBody : MemoryStream
        {
            private readonly TaskCompletionSource _delta = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly object _gate = new();

            public Task DeltaWritten => _delta.Task;

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (_gate) base.Write(buffer, offset, count);
                if (Encoding.UTF8.GetString(buffer, offset, count).Contains("\"type\":\"delta\"")) _delta.TrySetResult();
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            {
                var bytes = buffer.ToArray();
                Write(bytes, 0, bytes.Length);
                return ValueTask.CompletedTask;
            }

            public List<JsonElement> Events()
            {
                string text;
                lock (_gate) text = Encoding.UTF8.GetString(ToArray());
                return text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                    .Where(chunk => chunk.StartsWith("data: "))
                    .Select(chunk => JsonDocument.Parse(chunk["data: ".Length..]).RootElement.Clone())
                    .ToList();
            }
        }
    }
}
