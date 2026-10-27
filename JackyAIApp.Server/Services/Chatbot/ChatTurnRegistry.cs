using System.Collections.Concurrent;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// The chat turns being answered right now, so the reader can stop one. Closing the browser's
    /// request alone is not enough: Cloudflare sits between the browser and the app, and the app may
    /// not hear that the browser went away until the answer is complete — still generating, and still
    /// billing, the whole time. A stop asked for through the API cancels the turn on the spot.
    /// <para>
    /// In memory, so it covers one app instance — which is what the site runs on (one B1 instance).
    /// </para>
    /// </summary>
    public class ChatTurnRegistry
    {
        private readonly ConcurrentDictionary<string, Running> _turns = new();

        /// <summary>How long a new turn waits for the one it stopped to save what it keeps.</summary>
        public static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(5);

        // A turn's stop, and its end: done once it has saved what it keeps and left the registry.
        internal sealed record Running(CancellationTokenSource Cts, TaskCompletionSource Done);

        private static string Key(string userId, string conversationId) => $"{userId}\n{conversationId}";

        /// <summary>
        /// Registers a turn of a user's conversation; dispose the result when the turn is over. A turn
        /// still running in the same conversation is stopped: a conversation answers one turn at a time.
        /// </summary>
        public Turn Begin(string userId, string conversationId)
        {
            var key = Key(userId, conversationId);
            var running = new Running(new CancellationTokenSource(), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            _turns.AddOrUpdate(key, running, (_, previous) =>
            {
                TryCancel(previous.Cts);
                return running;
            });
            return new Turn(this, key, running);
        }

        /// <summary>Stops the turn being answered in a user's conversation; false when there is none.</summary>
        public bool Stop(string userId, string conversationId) =>
            _turns.TryGetValue(Key(userId, conversationId), out var running) && TryCancel(running.Cts);

        /// <summary>
        /// Stops the turn being answered in a user's conversation, if any, and waits (at most
        /// <paramref name="timeout"/>) until it has saved what it keeps. A new message, or an edit that
        /// rewinds the history, comes after this: otherwise the stopped turn's answer, saved a moment
        /// later, would land after the new question — two questions in a row, then two answers glued
        /// together in every later request. That is the reader's own "stop and ask this instead", which
        /// the client sends as soon as it has stopped, and also a second tab on the same conversation.
        /// </summary>
        public async Task SettleAsync(string userId, string conversationId, TimeSpan timeout, CancellationToken ct)
        {
            if (!_turns.TryGetValue(Key(userId, conversationId), out var running)) return;
            TryCancel(running.Cts);
            try
            {
                await running.Done.Task.WaitAsync(timeout, ct);
            }
            catch (TimeoutException)
            {
                // A turn that does not end in time does not hold the next one back for good.
            }
        }

        // The turn may end (and dispose its source) between being looked up and being cancelled.
        private static bool TryCancel(CancellationTokenSource cts)
        {
            try
            {
                cts.Cancel();
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public sealed class Turn : IDisposable
        {
            private readonly ChatTurnRegistry _registry;
            private readonly string _key;
            private readonly Running _running;

            internal Turn(ChatTurnRegistry registry, string key, Running running)
            {
                _registry = registry;
                _key = key;
                _running = running;
            }

            /// <summary>Cancelled when the turn is stopped.</summary>
            public CancellationToken Token => _running.Cts.Token;

            /// <summary>Call once the turn has saved what it keeps: a turn waiting in <see cref="SettleAsync"/> goes on.</summary>
            public void Dispose()
            {
                // Only this turn's own entry: a newer turn may have taken the key since.
                _registry._turns.TryRemove(new KeyValuePair<string, Running>(_key, _running));
                _running.Cts.Dispose();
                _running.Done.TrySetResult();
            }
        }
    }
}
