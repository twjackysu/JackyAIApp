using Anthropic.Models.Beta.Messages;
using System.Text.Json;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// Runs the tool calls of one reply that the server runs itself (a profile's <see cref="IAiTool"/>s,
    /// not the tw-stock tools Anthropic's MCP connector runs): one after another, each within
    /// <see cref="CallTimeout"/>, telling the reader as each ends (<c>tool_result</c>), and gives back
    /// the tool_result blocks the next request carries. Every result goes back to the model wrapped
    /// as untrusted data (<see cref="Wrap"/>): what a tool returns may come from anyone, and an
    /// instruction inside it is not the reader's.
    /// <para>
    /// A call of a tool that asks first (by default one whose effect is not <see cref="ToolEffect.Read"/>,
    /// see <see cref="ToolPermissions"/>) runs only once the reader allows it: the
    /// reply's such calls are put to them together (<c>approval_required</c>), and the turn waits in its
    /// own stream (<see cref="ChatApprovalRegistry"/>, at most <see cref="ApprovalTimeout"/>, a comment
    /// line every <see cref="PingEvery"/> so the stream is not closed as idle). A call they decline, or
    /// leave unanswered, is reported to the model as not done.
    /// </para>
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
        internal const string DeclinedMessage = "使用者拒絕了這個動作，沒有執行。不要再次嘗試同樣的動作，除非使用者明確要求。";
        internal const string ApprovalTimedOutMessage = "使用者沒有在時限內確認這個動作，沒有執行。請告訴使用者需要時可以再要求一次。";

        /// <summary>How long a turn waits for the reader to allow its writes before counting them as declined.</summary>
        public static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(3);

        /// <summary>How often a waiting stream sends a comment line.</summary>
        public static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(20);
        internal const string TruncatedNote = "\n[結果過長，以上僅為前段。]";

        // As the API's own JSON (Program.cs): camelCase, enums by name — a card may carry a DTO the
        // client also gets from an endpoint (a chart's summarize plan).
        private static readonly JsonSerializerOptions CardJson = new(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        private readonly Dictionary<string, IAiTool> _byName;
        private readonly IReadOnlySet<string> _askFirst;
        private readonly SseStream _sse;
        private readonly AiToolContext _context;
        private readonly ChatApprovalRegistry _approvals;
        private readonly TimeProvider _time;
        private readonly ILogger _logger;

        /// <param name="askFirst">The tools whose calls wait for the reader (their permission, see
        /// <see cref="ToolPermissions"/>).</param>
        /// <param name="tainted">Whether an earlier turn of the conversation read outside text already
        /// (<c>ChatConversation.ReadOutsideText</c>): its answers, sent again as history, may repeat it.</param>
        public ClientToolRunner(
            IReadOnlyList<IAiTool> tools, IReadOnlySet<string> askFirst, bool tainted, SseStream sse, AiToolContext context,
            ChatApprovalRegistry approvals, TimeProvider time, ILogger logger)
        {
            Tainted = tainted;
            _byName = tools.ToDictionary(t => t.Descriptor.Name, StringComparer.Ordinal);
            _askFirst = askFirst;
            _sse = sse;
            _context = context;
            _approvals = approvals;
            _time = time;
            _logger = logger;
        }

        /// <summary>
        /// Whether the conversation has read outside text: in this turn (an outside connector's result, or a
        /// site tool's marked <see cref="AiToolDescriptor.ReadsOutsideText"/>) or an earlier one. Once it has,
        /// every call that is not a read waits for the reader, whatever its permission says.
        /// </summary>
        public bool Tainted { get; private set; }

        private bool AsksFirst(IAiTool tool) =>
            _askFirst.Contains(tool.Descriptor.Name) || (Tainted && tool.Descriptor.Effect != ToolEffect.Read);

        /// <summary>The cards the reader was shown so far, in order (JSON as sent), to keep with the answer.</summary>
        public List<JsonElement> Cards { get; } = [];

        /// <summary>The name the reader is shown for a call of this tool, or null if it is not one of these.</summary>
        public string? DisplayNameOf(string toolName) =>
            _byName.TryGetValue(toolName, out var tool) ? tool.Descriptor.DisplayName : null;

        /// <summary>Runs the calls in order; the reader stopping the turn or leaving stops them (<paramref name="ct"/>).</summary>
        public async Task<List<BetaContentBlockParam>> RunAsync(IReadOnlyList<BetaToolUseBlock> calls, CancellationToken ct)
        {
            var asked = new List<(BetaToolUseBlock Call, IAiTool Tool)>();
            foreach (var call in calls)
                if (_byName.TryGetValue(call.Name, out var tool) && AsksFirst(tool))
                    asked.Add((call, tool));
            var (allowed, timedOut) = asked.Count > 0
                ? await AskAsync(asked, ct)
                : ((IReadOnlyDictionary<string, bool>)new Dictionary<string, bool>(), false);

            var results = new List<BetaContentBlockParam>();
            foreach (var call in calls)
            {
                var started = _time.GetTimestamp();
                var result = allowed.TryGetValue(call.ID, out var yes) && !yes
                    ? AiToolResult.Error(timedOut ? ApprovalTimedOutMessage : DeclinedMessage)
                    : await RunOneAsync(call, ct);
                var durationMs = (long)_time.GetElapsedTime(started).TotalMilliseconds;

                var card = result.IsError || result.Card == null ? null : CardOf(call.Name, result.Card);
                if (card != null) Cards.Add(card.Value);
                await _sse.WriteAsync(new
                {
                    type = "tool_result",
                    id = call.ID,
                    name = call.Name,
                    ok = !result.IsError,
                    card,
                    durationMs,
                    navigate = NavigateOf(call.Name, result),
                }, ct);
                // What an outside connector (or someone's calendar invite) said may be written to steer the model;
                // from here on in the turn, nothing that changes anything runs without the reader (docs/ai-native/design/06).
                if (_byName.TryGetValue(call.Name, out var ran) && ran.Descriptor.ReadsOutsideText)
                    Tainted = true;
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
        /// Where the reader's page goes now: only a site tool's own (a connector's tool never moves the reader's page),
        /// only a plain path on this site, and nothing after a failure.
        /// </summary>
        private string? NavigateOf(string toolName, AiToolResult result) =>
            !result.IsError && result.Navigate is { } path && ToolCardGuard.IsSitePath(path) &&
            _byName.TryGetValue(toolName, out var tool) && tool.Descriptor.Connector == AiToolDescriptor.SiteConnector
                ? path
                : null;

        /// <summary>The card as the reader may be sent it (<see cref="ToolCardGuard"/>), or null.</summary>
        private JsonElement? CardOf(string toolName, object card)
        {
            var checkedCard = ToolCardGuard.Check(JsonSerializer.SerializeToElement(card, CardJson));
            if (checkedCard == null)
                _logger.LogWarning("Chat tool {Tool} showed a card that is not sent, in conversation {ConversationId}",
                    toolName, _context.ConversationId);
            return checkedCard;
        }

        /// <summary>What a site tool says one call would do, for the card; none from a connector's tool, or on any failure.</summary>
        private string? Summary(BetaToolUseBlock call, IAiTool tool)
        {
            if (tool is not IDescribesCall describes || tool.Descriptor.Connector != AiToolDescriptor.SiteConnector) return null;
            try
            {
                var text = describes.Describe(JsonSerializer.SerializeToElement(call.Input), _context)?.Trim();
                return string.IsNullOrEmpty(text) ? null : text.Length > 200 ? text[..200] : text;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Puts <paramref name="calls"/> to the reader and waits for their decisions, by call id (true =
        /// allowed); every one a no if none came within <see cref="ApprovalTimeout"/> (TimedOut). The
        /// reader stopping the turn or leaving ends the wait (<paramref name="ct"/>).
        /// </summary>
        private async Task<(IReadOnlyDictionary<string, bool> Allowed, bool TimedOut)> AskAsync(
            IReadOnlyList<(BetaToolUseBlock Call, IAiTool Tool)> calls, CancellationToken ct)
        {
            using var waiting = _approvals.Begin(_context.UserId, _context.ConversationId, calls.Select(c => c.Call.ID).ToList());
            await _sse.WriteAsync(new
            {
                type = "approval_required",
                pendingId = waiting.PendingId,
                tainted = Tainted,
                calls = calls.Select(c => new
                {
                    toolUseId = c.Call.ID,
                    connector = c.Tool.Descriptor.Connector,
                    connectorName = c.Tool.Descriptor.ConnectorName,
                    tool = c.Call.Name,
                    displayName = c.Tool.Descriptor.DisplayName,
                    effect = c.Tool.Descriptor.Effect.WireName(),
                    input = JsonSerializer.SerializeToElement(c.Call.Input),
                    summary = Summary(c.Call, c.Tool),
                }).ToList(),
            }, ct);

            var started = _time.GetTimestamp();
            IReadOnlyDictionary<string, bool> decided;
            var timedOut = false;
            while (true)
            {
                var left = ApprovalTimeout - _time.GetElapsedTime(started);
                if (left <= TimeSpan.Zero)
                {
                    // Settle it as a no, unless the reader's answer got in first: then that answer stands, and a
                    // POST after this point is refused rather than taken and ignored.
                    var none = calls.ToDictionary(c => c.Call.ID, _ => false, StringComparer.Ordinal);
                    if (waiting.Decide(none))
                    {
                        timedOut = true;
                        decided = none;
                        _logger.LogInformation("Chat approval {PendingId} in conversation {ConversationId} timed out",
                            waiting.PendingId, _context.ConversationId);
                    }
                    else
                        decided = await waiting.Decided;
                    break;
                }
                try
                {
                    decided = await waiting.Decided.WaitAsync(left < PingEvery ? left : PingEvery, _time, ct);
                    break;
                }
                catch (TimeoutException)
                {
                    await _sse.WriteCommentAsync("ping", ct);
                }
            }

            await _sse.WriteAsync(new
            {
                type = "approval_resolved",
                pendingId = waiting.PendingId,
                decisions = calls.Select(c => new { toolUseId = c.Call.ID, approve = decided[c.Call.ID] }).ToList(),
            }, ct);
            return (decided, timedOut);
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
