namespace JackyAIApp.Server.Services
{
    /// <summary>A usage charge taken during the current request.</summary>
    public readonly record struct UsageCharge(string UserId, ulong Cost, string Feature);

    /// <summary>
    /// The current request's usage charges, and the one rule that decides whether they are given back.
    /// Scoped: one per request. <see cref="UsageLimitService"/> records every charge it takes, the AI
    /// clients report when a provider billed for a call, and <c>UsageSettlementMiddleware</c> settles
    /// at the end of the request.
    ///
    /// The daily budget is a free trial allowance whose AI calls the author pays for. A charge is
    /// refunded only when the request failed <em>and</em> no AI provider billed anything for it: it
    /// failed before any AI call (bad input, nothing to serve, a bug on our side), or the provider
    /// rejected the call or it timed out without a response. Once a provider has billed, the charge
    /// stands even if the answer could not be used (empty, unparseable, rejected by a validator):
    /// refunding those would let a failure that repeats on every try — a prompt bug, a model change —
    /// cost money with nothing to stop it. Successful requests are never refunded, AI or not.
    /// </summary>
    public sealed class UsageLedger
    {
        private readonly object _lock = new();
        private readonly List<UsageCharge> _charges = [];
        private bool _aiBilled;
        private bool _failed;
        private bool _settled;

        public void RecordCharge(string userId, ulong cost, string feature)
        {
            lock (_lock) _charges.Add(new UsageCharge(userId, cost, feature));
        }

        /// <summary>An AI provider billed for a call this request made.</summary>
        public void MarkAiBilled() => _aiBilled = true;

        /// <summary>
        /// The request failed although its HTTP status will not show it — a streamed response that
        /// had already sent 200 before the failure.
        /// </summary>
        public void MarkFailed() => _failed = true;

        /// <summary>
        /// The charges to refund now that the request is over: all of them when it failed
        /// (<paramref name="responseFailed"/> or <see cref="MarkFailed"/>) and no AI provider billed,
        /// otherwise none. Answers once; later calls return nothing, so nothing is refunded twice.
        /// </summary>
        public IReadOnlyList<UsageCharge> TakeRefunds(bool responseFailed)
        {
            lock (_lock)
            {
                if (_settled) return [];
                _settled = true;
                return (responseFailed || _failed) && !_aiBilled ? _charges.ToList() : [];
            }
        }
    }
}
