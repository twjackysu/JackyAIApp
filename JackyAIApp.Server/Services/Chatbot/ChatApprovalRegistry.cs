using System.Collections.Concurrent;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>The reader's yes or no on one call of a reply (<c>toolUseId</c> is the call's id).</summary>
    public sealed record ChatApprovalDecision(string ToolUseId, bool Approve);

    /// <summary>
    /// The writes the assistant is waiting for the reader to allow (docs/ai-native/design/04 §1.3). A turn
    /// that wants to change something asks with <c>approval_required</c> and waits in its own request, in
    /// the same stream, until the decisions arrive (<c>POST conversations/{id}/approvals/{pendingId}</c>)
    /// — so nothing of the turn (its replies with their thinking signatures, its rounds and its charge)
    /// has to be kept anywhere in the meantime.
    /// <para>
    /// In memory, like <see cref="ChatTurnRegistry"/>, so it covers one app instance — which is what the
    /// site runs on. Reloading the page loses a question still waiting; the turn then times out as a no.
    /// </para>
    /// </summary>
    public class ChatApprovalRegistry
    {
        private readonly ConcurrentDictionary<string, Waiting> _waiting = new(StringComparer.Ordinal);

        /// <summary>
        /// Starts waiting for the reader's decisions on <paramref name="toolUseIds"/>, the calls of one reply
        /// in a user's conversation; dispose the result when the turn stops waiting.
        /// </summary>
        public Waiting Begin(string userId, string conversationId, IReadOnlyCollection<string> toolUseIds)
        {
            var waiting = new Waiting(this, Guid.NewGuid().ToString("N"), userId, conversationId, toolUseIds.ToHashSet(StringComparer.Ordinal));
            _waiting[waiting.PendingId] = waiting;
            return waiting;
        }

        /// <summary>
        /// Hands the reader's decisions to the turn waiting for them. Only that user, in that conversation,
        /// can; a call they did not decide on counts as a no, and a decision on a call the question did not
        /// ask about is ignored. False when nothing is waiting under <paramref name="pendingId"/> for them
        /// (it was decided already, timed out, or is someone else's).
        /// </summary>
        public bool Resolve(string userId, string conversationId, string pendingId, IReadOnlyList<ChatApprovalDecision> decisions)
        {
            if (!_waiting.TryGetValue(pendingId, out var waiting) ||
                waiting.UserId != userId || waiting.ConversationId != conversationId)
                return false;

            var approved = decisions.Where(d => d.Approve).Select(d => d.ToolUseId).ToHashSet(StringComparer.Ordinal);
            var decided = waiting.ToolUseIds.ToDictionary(id => id, approved.Contains, StringComparer.Ordinal);
            return waiting.Decide(decided);
        }

        public sealed class Waiting : IDisposable
        {
            private readonly ChatApprovalRegistry _registry;
            private readonly TaskCompletionSource<IReadOnlyDictionary<string, bool>> _decided =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal Waiting(ChatApprovalRegistry registry, string pendingId, string userId, string conversationId,
                IReadOnlySet<string> toolUseIds)
            {
                _registry = registry;
                PendingId = pendingId;
                UserId = userId;
                ConversationId = conversationId;
                ToolUseIds = toolUseIds;
            }

            /// <summary>What the reader's answer names the question by.</summary>
            public string PendingId { get; }

            internal string UserId { get; }
            internal string ConversationId { get; }
            internal IReadOnlySet<string> ToolUseIds { get; }

            /// <summary>The decisions, by call id (every call asked about, true = allowed), once they arrive.</summary>
            public Task<IReadOnlyDictionary<string, bool>> Decided => _decided.Task;

            internal bool Decide(IReadOnlyDictionary<string, bool> decisions) => _decided.TrySetResult(decisions);

            public void Dispose()
            {
                // An answer that comes after the turn stopped waiting finds nothing.
                _registry._waiting.TryRemove(new KeyValuePair<string, Waiting>(PendingId, this));
                _decided.TrySetCanceled();
            }
        }
    }
}
