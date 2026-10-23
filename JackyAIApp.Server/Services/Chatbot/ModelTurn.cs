using Anthropic;
using Anthropic.Models.Beta.Messages;
using System.Text;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// How a turn's requests ended, before anything is made of it (that is <see cref="TurnVerdict"/>).
    /// </summary>
    /// <param name="Text">The answer text the reader was sent.</param>
    /// <param name="StopReason">The last request's stop_reason, if it got that far.</param>
    /// <param name="Stopped">The reader stopped the turn, or left.</param>
    /// <param name="ErrorCode">Why a request failed (<see cref="ChatErrorCodes"/>), or null.</param>
    /// <param name="ToolsCalled">The tw-stock tools the turn called, in order.</param>
    /// <param name="ToolSearches">How often it searched for a tool.</param>
    internal sealed record TurnOutcome(
        string Text, bool StreamingStarted, BetaStopReason? StopReason, bool Stopped, string? ErrorCode,
        TokenTotals Tokens, IReadOnlyList<string> ToolsCalled, int ToolSearches);

    /// <summary>
    /// The requests of one chat turn: streams the model's answer to the reader as it comes, retries a
    /// pause_turn that produced nothing, uploads the files again once if the Files API no longer has
    /// one, and says how it all ended. A new one for each turn.
    /// </summary>
    internal sealed class ModelTurn(
        AnthropicClient client, SseStream sse, UsageLedger usageLedger, ILogger logger, string conversationId)
    {
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

        /// <param name="reuploadFiles">The request again, with the conversation's files uploaded again.</param>
        /// <param name="ct">Cancelled when the reader stops the turn or leaves.</param>
        public async Task<TurnOutcome> RunAsync(
            MessageCreateParams request, Func<CancellationToken, Task<MessageCreateParams>> reuploadFiles, CancellationToken ct)
        {
            var filesReuploaded = false;
            var stopped = false;
            string? errorCode = null;
            for (var attempt = 0; ; attempt++)
            {
                _stopReason = null;
                var usage = new RequestUsage();
                try
                {
                    await foreach (var evt in client.Beta.Messages.CreateStreaming(request, ct))
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
                    request = await reuploadFiles(ct);
                    continue;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    errorCode = ChatErrorCodes.For(ex);
                    // The browser only gets the code; this is where the actual error is kept.
                    logger.LogError(ex,
                        "Anthropic chat streaming error for conversation {ConversationId}; reported as {ErrorCode}",
                        conversationId, errorCode);
                    break;
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    // The reader stopped the turn, or left. Whatever the SDK threw on the way out (it
                    // need not be an OperationCanceledException), the turn ends here.
                    stopped = true;
                    break;
                }
                finally
                {
                    _tokens = _tokens.Plus(usage);
                }

                // pause_turn = the MCP connector's server-side tool loop hit its internal
                // iteration cap before producing any answer text. Nothing has reached the client
                // yet (_streamingStarted is false), so a from-scratch retry is invisible to the
                // user — NOT a true mid-conversation resume (the SDK has no block-accumulation
                // helper to reconstruct the partial assistant turn), just "try the whole request
                // again," which is enough for the common case of a transient MCP hiccup.
                if (!_streamingStarted && _stopReason == BetaStopReason.PauseTurn && attempt < MaxPauseTurnRetries)
                {
                    logger.LogWarning(
                        "Anthropic chat turn hit pause_turn with no output for conversation {ConversationId}; retrying (attempt {Attempt}/{Max})",
                        conversationId, attempt + 1, MaxPauseTurnRetries);
                    continue;
                }

                break;
            }

            return new TurnOutcome(
                _text.ToString(), _streamingStarted, _stopReason, stopped, errorCode, _tokens, _toolsCalled, _toolSearches);
        }

        private async Task OnEventAsync(BetaRawMessageStreamEvent evt, RequestUsage usage, CancellationToken ct)
        {
            if (evt.TryPickContentBlockDelta(out var blockDelta) && blockDelta.Delta.TryPickText(out var textDelta))
            {
                _streamingStarted = true;
                _text.Append(textDelta.Text);
                await sse.WriteAsync(new { type = "delta", text = textDelta.Text }, ct);
            }
            else if (evt.TryPickContentBlockStart(out var blockStart))
            {
                if (blockStart.ContentBlock.TryPickBetaMcpToolUse(out var toolUse))
                {
                    _toolsCalled.Add(toolUse.Name);
                    await sse.WriteAsync(new { type = "tool_use", name = toolUse.Name }, ct);
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
