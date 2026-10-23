using Anthropic.Models.Beta.Messages;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// What a turn's outcome means: whether it answered, the code it fails with, whether the answer
    /// ran into the output cap, and what is kept as the assistant's message.
    /// </summary>
    /// <param name="ErrorCode">Null for an answer, and for a turn the reader stopped.</param>
    /// <param name="Saved">The assistant message to keep, or null to keep none.</param>
    internal sealed record TurnVerdict(bool Success, string? ErrorCode, bool Truncated, bool Stopped, string? Saved)
    {
        public static TurnVerdict Of(TurnOutcome outcome)
        {
            // A stopped turn keeps what was said before the stop, marked as cut off — even if that is
            // nothing — so the reader sees it on reopening the conversation, and the model knows on the
            // next turn that this answer did not finish (as Claude Code records an interrupted turn).
            if (outcome.Stopped)
                return new(false, null, false, true, AnthropicChatService.StoppedContent(outcome.Text));

            // A request that failed after some text keeps that text as it is.
            if (outcome.ErrorCode != null)
                return Failed(outcome.ErrorCode, outcome.Text.Length > 0 ? outcome.Text : null);

            // The stream can complete with no exception yet produce zero text — e.g. the MCP
            // tool call plus follow-up analysis ran out of MaxTokens before any answer text was
            // written. Treat that as a failure rather than silently sending "done" with nothing
            // to show (previously left the chat bubble blank forever with no error surfaced).
            if (outcome.Text.Length == 0)
                return Failed(AnthropicChatService.NoAnswerCode(outcome.StopReason), null);

            // A decline can also arrive after some text has streamed. That text is a cut-off
            // answer, so it is reported as a failure and not kept as the assistant's turn.
            if (outcome.StopReason == BetaStopReason.Refusal)
                return Failed(ChatErrorCodes.Refused, null);

            // An answer that ran into the output cap is kept, but the reader is told it is cut short.
            return new(true, null, outcome.StopReason == BetaStopReason.MaxTokens, false, outcome.Text);
        }

        private static TurnVerdict Failed(string errorCode, string? saved) => new(false, errorCode, false, false, saved);
    }
}
