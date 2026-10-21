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
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _turns = new();

        private static string Key(string userId, string conversationId) => $"{userId}\n{conversationId}";

        /// <summary>
        /// Registers a turn of a user's conversation; dispose the result when the turn is over. A turn
        /// still running in the same conversation is stopped: a conversation answers one turn at a time.
        /// </summary>
        public Turn Begin(string userId, string conversationId)
        {
            var key = Key(userId, conversationId);
            var cts = new CancellationTokenSource();
            _turns.AddOrUpdate(key, cts, (_, previous) =>
            {
                TryCancel(previous);
                return cts;
            });
            return new Turn(this, key, cts);
        }

        /// <summary>Stops the turn being answered in a user's conversation; false when there is none.</summary>
        public bool Stop(string userId, string conversationId) =>
            _turns.TryGetValue(Key(userId, conversationId), out var cts) && TryCancel(cts);

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

        public sealed class Turn(ChatTurnRegistry registry, string key, CancellationTokenSource cts) : IDisposable
        {
            /// <summary>Cancelled when the turn is stopped.</summary>
            public CancellationToken Token => cts.Token;

            public void Dispose()
            {
                // Only this turn's own entry: a newer turn may have taken the key since.
                registry._turns.TryRemove(new KeyValuePair<string, CancellationTokenSource>(key, cts));
                cts.Dispose();
            }
        }
    }
}
