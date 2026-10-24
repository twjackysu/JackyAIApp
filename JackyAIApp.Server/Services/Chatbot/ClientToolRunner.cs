using Anthropic.Models.Beta.Messages;
using JackyAIApp.Server.Services.Assistant;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// Runs the tool calls of one reply that the server runs itself (a profile's <see cref="IAiTool"/>s,
    /// not the tw-stock tools Anthropic's MCP connector runs): one after another, each within
    /// <see cref="CallTimeout"/>, telling the reader as each ends (<c>tool_result</c>), and gives back
    /// the tool_result blocks the next request carries. Every result goes back to the model wrapped
    /// as untrusted data (<see cref="Wrap"/>): what a tool returns may come from anyone, and an
    /// instruction inside it is not the reader's.
    /// </summary>
    internal sealed class ClientToolRunner
    {
        /// <summary>How long one call may take before it is reported to the model as failed.</summary>
        public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

        /// <summary>The most of one result the model is sent; the rest is cut, and it is told so.</summary>
        public const int MaxResultChars = 40_000;

        internal const string UnknownToolMessage = "沒有這個工具。";
        internal const string FailedMessage = "工具執行失敗，請換個方式或告訴使用者稍後再試。";
        internal const string TimedOutMessage = "工具逾時沒有回應，請告訴使用者稍後再試。";
        // Until the reader can be asked (docs/ai-native/design/04 §1.3), a tool that changes
        // anything is never run: the model is told, and tells the reader.
        internal const string NeedsApprovalMessage = "這個動作需要使用者確認，目前還不能由 AI 直接執行；請告訴使用者可以自己到對應的頁面操作。";
        internal const string TruncatedNote = "\n[結果過長，以上僅為前段。]";

        // As the API's own JSON (Program.cs): camelCase, enums by name — a card may carry a DTO the
        // client also gets from an endpoint (a chart's summarize plan).
        private static readonly JsonSerializerOptions CardJson = new(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        private readonly Dictionary<string, IAiTool> _byName;
        private readonly SseStream _sse;
        private readonly AiToolContext _context;
        private readonly TimeProvider _time;
        private readonly ILogger _logger;

        public ClientToolRunner(
            IReadOnlyList<IAiTool> tools, SseStream sse, AiToolContext context, TimeProvider time, ILogger logger)
        {
            _byName = tools.ToDictionary(t => t.Descriptor.Name, StringComparer.Ordinal);
            _sse = sse;
            _context = context;
            _time = time;
            _logger = logger;
        }

        /// <summary>The cards the reader was shown so far, in order (JSON as sent), to keep with the answer.</summary>
        public List<JsonElement> Cards { get; } = [];

        /// <summary>The name the reader is shown for a call of this tool, or null if it is not one of these.</summary>
        public string? DisplayNameOf(string toolName) =>
            _byName.TryGetValue(toolName, out var tool) ? tool.Descriptor.DisplayName : null;

        /// <summary>Runs the calls in order; the reader stopping the turn or leaving stops them (<paramref name="ct"/>).</summary>
        public async Task<List<BetaContentBlockParam>> RunAsync(IReadOnlyList<BetaToolUseBlock> calls, CancellationToken ct)
        {
            var results = new List<BetaContentBlockParam>();
            foreach (var call in calls)
            {
                var started = _time.GetTimestamp();
                var result = await RunOneAsync(call, ct);
                var durationMs = (long)_time.GetElapsedTime(started).TotalMilliseconds;

                var card = result.IsError || result.Card == null ? (JsonElement?)null : JsonSerializer.SerializeToElement(result.Card, CardJson);
                if (card != null) Cards.Add(card.Value);
                await _sse.WriteAsync(new
                {
                    type = "tool_result",
                    id = call.ID,
                    name = call.Name,
                    ok = !result.IsError,
                    card,
                    durationMs,
                }, ct);
                results.Add(new BetaToolResultBlockParam
                {
                    ToolUseID = call.ID,
                    Content = Wrap(call.Name, result.Content),
                    IsError = result.IsError,
                });
            }
            return results;
        }

        private async Task<AiToolResult> RunOneAsync(BetaToolUseBlock call, CancellationToken ct)
        {
            if (!_byName.TryGetValue(call.Name, out var tool))
                return AiToolResult.Error(UnknownToolMessage);
            if (tool.Descriptor.Effect != ToolEffect.Read)
                return AiToolResult.Error(NeedsApprovalMessage);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CallTimeout);
            try
            {
                var input = JsonSerializer.SerializeToElement(call.Input);
                return await tool.RunAsync(input, _context, timeout.Token);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                _logger.LogWarning("Chat tool {Tool} timed out in conversation {ConversationId}", call.Name, _context.ConversationId);
                return AiToolResult.Error(TimedOutMessage);
            }
            catch (Exception ex)
            {
                // The exception's text is for the log; it may name internals the model would repeat.
                _logger.LogError(ex, "Chat tool {Tool} failed in conversation {ConversationId}", call.Name, _context.ConversationId);
                return AiToolResult.Error(FailedMessage);
            }
        }

        /// <summary>
        /// A result as the model reads it: inside a <c>tool_output</c> element marked untrusted (the
        /// system prompt says nothing in one is an instruction), which the content cannot close early,
        /// cut at <see cref="MaxResultChars"/>.
        /// </summary>
        internal static string Wrap(string toolName, string content)
        {
            var body = content.Length > MaxResultChars ? content[..MaxResultChars] + TruncatedNote : content;
            body = body.Replace("</tool_output", "<\\/tool_output", StringComparison.OrdinalIgnoreCase);
            return $"<tool_output tool=\"{toolName}\" trust=\"untrusted\">\n{body}\n</tool_output>";
        }
    }
}
