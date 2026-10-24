using Anthropic;
using Anthropic.Helpers;
using Anthropic.Models.Beta.Messages;
using System.Text;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// How a turn's requests ended, before anything is made of it (that is <see cref="TurnVerdict"/>).
    /// </summary>
    /// <param name="Text">The answer text the reader was sent.</param>
    /// <param name="StreamingStarted">Whether any answer text reached the reader.</param>
    /// <param name="StopReason">The last request's stop_reason, if it got that far.</param>
    /// <param name="Stopped">The reader stopped the turn, or left.</param>
    /// <param name="ErrorCode">Why a request failed (<see cref="ChatErrorCodes"/>), or null.</param>
    /// <param name="Tokens">The turn's tokens over all its requests.</param>
    /// <param name="ToolsCalled">The tools the turn called, in order (tw-stock's and the profile's own).</param>
    /// <param name="ToolSearches">How often it searched for a tool.</param>
    internal sealed record TurnOutcome(
        string Text, bool StreamingStarted, BetaStopReason? StopReason, bool Stopped, string? ErrorCode,
        TokenTotals Tokens, IReadOnlyList<string> ToolsCalled, int ToolSearches);

    /// <summary>
    /// The requests of one chat turn: streams the model's answer to the reader as it comes, retries a
    /// pause_turn that produced nothing, uploads the files again once if the Files API no longer has
    /// one, and says how it all ended. A new one for each turn.
    /// <para>
    /// With tools of the profile's own (<paramref name="tools"/>), it is also the agent loop: a reply
    /// that stops to call them (stop_reason tool_use) is collected whole while it streams, the calls
    /// are run, and the next request sends that reply back unchanged (its thinking signatures
    /// included) with the results, up to <see cref="MaxToolRounds"/> requests. Without tools (Finance)
    /// a turn is one request, read exactly as before.
    /// </para>
    /// </summary>
    internal sealed class ModelTurn(
        AnthropicClient client, SseStream sse, UsageLedger usageLedger, ILogger logger, string conversationId,
        ClientToolRunner? tools = null)
    {
        /// <summary>The most requests one turn makes for its tools; the last one's answer ends the turn.</summary>
        internal const int MaxToolRounds = 8;

        // The MCP connector runs its tool-call loop server-side with an internal 10-iteration
        // cap; hitting it ends the turn with stop_reason=pause_turn before any answer text is
        // produced. Retrying the same request from scratch (not a true resume — see below) often
        // succeeds if the stall was a transient MCP-server hiccup. Bounded so a query that
        // genuinely needs an unusually large number of tool calls fails fast instead of looping.
        private const int MaxPauseTurnRetries = 2;

        private readonly StringBuilder _text = new();
        private readonly List<string> _toolsCalled = [];
        private int _toolSearches;
        private bool _streamingStarted;
        private BetaStopReason? _stopReason;
        private TokenTotals _tokens;
        private bool _paragraphBreakDue;

        /// <param name="request">The turn's first request.</param>
        /// <param name="reuploadFiles">The request again, with the conversation's files uploaded again.</param>
        /// <param name="ct">Cancelled when the reader stops the turn or leaves.</param>
        public async Task<TurnOutcome> RunAsync(
            MessageCreateParams request, Func<CancellationToken, Task<MessageCreateParams>> reuploadFiles, CancellationToken ct)
        {
            var stopped = false;
            string? errorCode = null;
            for (var round = 1; ; round++)
            {
                BetaMessage? reply;
                (stopped, errorCode, request, reply) = await RequestAsync(
                    request, round == 1 ? reuploadFiles : null, collect: tools != null, ct);
                if (reply == null || stopped || errorCode != null || _stopReason != BetaStopReason.ToolUse) break;

                var calls = new List<BetaToolUseBlock>();
                foreach (var block in reply.Content)
                    if (block.TryPickToolUse(out var use)) calls.Add(use);
                if (calls.Count == 0) break;
                if (round >= MaxToolRounds)
                {
                    logger.LogWarning("Chat turn for conversation {ConversationId} still calling tools after {Rounds} requests; ending it",
                        conversationId, round);
                    break;
                }

                List<BetaContentBlockParam> results;
                try
                {
                    results = await tools!.RunAsync(calls, ct);
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    stopped = true;
                    break;
                }

                // Text the next reply writes is a new paragraph, not the end of the last one's sentence.
                _paragraphBreakDue = _text.Length > 0;
                request = request with
                {
                    Messages =
                    [
                        .. request.Messages,
                        // As the SDK's own tool runner does: the reply's blocks, through JSON, become
                        // the assistant turn sent back.
                        new BetaMessageParam
                        {
                            Role = Role.Assistant,
                            Content = new BetaMessageParamContent(
                                JsonSerializer.SerializeToElement(reply.Content.Select(b => b.Json).ToArray())),
                        },
                        new BetaMessageParam { Role = Role.User, Content = results },
                    ],
                };
            }

            return new TurnOutcome(
                _text.ToString(), _streamingStarted, _stopReason, stopped, errorCode, _tokens, _toolsCalled, _toolSearches);
        }

        /// <summary>
        /// One request of the turn, retried as the class says; the request it ended with (its files
        /// uploaded again, if they were). Only the first request of a turn uploads files again
        /// (<paramref name="reuploadFiles"/> null for the others): a later one carries tool rounds that
        /// a rebuilt request would not have. With <paramref name="collect"/>, the whole reply of a
        /// request that went through comes back too, for its tool calls.
        /// </summary>
        private async Task<(bool Stopped, string? ErrorCode, MessageCreateParams Request, BetaMessage? Reply)> RequestAsync(
            MessageCreateParams request, Func<CancellationToken, Task<MessageCreateParams>>? reuploadFiles,
            bool collect, CancellationToken ct)
        {
            var filesReuploaded = reuploadFiles == null;
            BetaMessageContentAggregator? aggregator = null;
            for (var attempt = 0; ; attempt++)
            {
                _stopReason = null;
                var usage = new RequestUsage();
                try
                {
                    var stream = client.Beta.Messages.CreateStreaming(request, ct);
                    // One aggregator per attempt: an aggregator collects a single stream.
                    aggregator = collect ? new BetaMessageContentAggregator() : null;
                    if (aggregator != null) stream = aggregator.CollectAsync(stream);
                    await foreach (var evt in stream)
                        await OnEventAsync(evt, usage, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && !_streamingStarted && !filesReuploaded &&
                                           ChatErrorCodes.IsMissingFile(ex))
                {
                    // A file_id the Files API no longer has: upload this conversation's files again from
                    // the attachment store and try once more. Nothing has reached the reader yet.
                    logger.LogWarning(ex, "Anthropic did not find a file of conversation {ConversationId}; uploading its files again",
                        conversationId);
                    filesReuploaded = true;
                    request = await reuploadFiles!(ct);
                    continue;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    var errorCode = ChatErrorCodes.For(ex);
                    // The browser only gets the code; this is where the actual error is kept.
                    logger.LogError(ex,
                        "Anthropic chat streaming error for conversation {ConversationId}; reported as {ErrorCode}",
                        conversationId, errorCode);
                    return (false, errorCode, request, null);
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    // The reader stopped the turn, or left. Whatever the SDK threw on the way out (it
                    // need not be an OperationCanceledException), the turn ends here.
                    return (true, null, request, null);
                }
                finally
                {
                    _tokens = _tokens.Plus(usage);
                }

                // pause_turn = the MCP connector's server-side tool loop hit its internal
                // iteration cap before producing any answer text. Nothing has reached the client
                // yet (_streamingStarted is false), so a from-scratch retry is invisible to the
                // user — NOT a true mid-conversation resume, just "try the whole request again,"
                // which is enough for the common case of a transient MCP hiccup.
                if (!_streamingStarted && _stopReason == BetaStopReason.PauseTurn && attempt < MaxPauseTurnRetries)
                {
                    logger.LogWarning(
                        "Anthropic chat turn hit pause_turn with no output for conversation {ConversationId}; retrying (attempt {Attempt}/{Max})",
                        conversationId, attempt + 1, MaxPauseTurnRetries);
                    continue;
                }

                return (false, null, request, aggregator?.Message());
            }
        }

        private async Task OnEventAsync(BetaRawMessageStreamEvent evt, RequestUsage usage, CancellationToken ct)
        {
            if (evt.TryPickContentBlockDelta(out var blockDelta) && blockDelta.Delta.TryPickText(out var textDelta))
            {
                _streamingStarted = true;
                var text = _paragraphBreakDue ? "\n\n" + textDelta.Text : textDelta.Text;
                _paragraphBreakDue = false;
                _text.Append(text);
                await sse.WriteAsync(new { type = "delta", text }, ct);
            }
            else if (evt.TryPickContentBlockStart(out var blockStart))
            {
                if (blockStart.ContentBlock.TryPickBetaMcpToolUse(out var toolUse))
                {
                    _toolsCalled.Add(toolUse.Name);
                    await sse.WriteAsync(new { type = "tool_use", name = toolUse.Name }, ct);
                }
                // One of the profile's own tools: the reader sees it start; its result follows from
                // ClientToolRunner once the reply has ended and the call is run.
                else if (tools != null && blockStart.ContentBlock.TryPickBetaToolUse(out var clientToolUse))
                {
                    _toolsCalled.Add(clientToolUse.Name);
                    await sse.WriteAsync(new
                    {
                        type = "tool_use",
                        name = clientToolUse.Name,
                        id = clientToolUse.ID,
                        displayName = tools.DisplayNameOf(clientToolUse.Name),
                    }, ct);
                }
                // A tool search (server_tool_use, answered by a tool_search_tool_result
                // block) carries no text for the reader; it is only counted.
                else if (blockStart.ContentBlock.TryPickBetaServerToolUse(out _))
                {
                    _toolSearches++;
                }
            }
            else if (evt.TryPickStart(out var startEvt))
            {
                // Anthropic bills from here on, whether or not any answer text follows.
                usageLedger.MarkAiBilled();
                var u = startEvt.Message.Usage;
                usage.Update(u.InputTokens, u.CacheCreationInputTokens, u.CacheReadInputTokens, u.OutputTokens);
            }
            else if (evt.TryPickDelta(out var msgDelta))
            {
                if (msgDelta.Usage is { } u)
                    usage.Update(u.InputTokens, u.CacheCreationInputTokens, u.CacheReadInputTokens, u.OutputTokens);
                if (msgDelta.Delta != null) _stopReason = msgDelta.Delta.StopReason?.Value();
            }
        }
    }
}
