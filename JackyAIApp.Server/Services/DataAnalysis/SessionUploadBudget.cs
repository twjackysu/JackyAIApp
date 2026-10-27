using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// The estimated bytes every live upload session holds, against
    /// <see cref="SessionUploadOptions.MaxTotalBytes"/>. Kept here rather than as a SizeLimit on the shared
    /// IMemoryCache, which would make every other cache entry in the app declare a Size.
    /// A session reserves its bytes before it is stored and releases them from its cache entry's
    /// post-eviction callback (expiry or removal).
    ///
    /// Each caller (a signed-in user, else an address) may also hold at most
    /// <see cref="SessionUploadOptions.MaxBytesPerCaller"/> of it, so one visitor's few large uploads cannot
    /// take the whole budget from everyone else for the sessions' two hours (D-159).
    /// </summary>
    public sealed class SessionUploadBudget(IOptions<SessionUploadOptions> options)
    {
        private readonly long _maxBytes = options.Value.MaxTotalBytes;
        // Never below one upload's largest size, or a configuration raising that would refuse every such upload.
        private readonly long _maxBytesPerCaller = Math.Max(options.Value.MaxBytesPerCaller, options.Value.MaxSessionBytes);
        private readonly Dictionary<string, long> _byCaller = new(StringComparer.Ordinal);
        private readonly Lock _callersGate = new();
        private long _usedBytes;

        public long UsedBytes => Interlocked.Read(ref _usedBytes);

        public long RemainingBytes => Math.Max(0, _maxBytes - UsedBytes);

        /// <summary>
        /// Claims <paramref name="bytes"/> if they fit, atomically: two uploads racing for the last of the
        /// budget can't both get it.
        /// </summary>
        public bool TryReserve(long bytes)
        {
            while (true)
            {
                var used = Interlocked.Read(ref _usedBytes);
                if (used + bytes > _maxBytes) return false;
                if (Interlocked.CompareExchange(ref _usedBytes, used + bytes, used) == used) return true;
            }
        }

        public void Release(long bytes) => Interlocked.Add(ref _usedBytes, -bytes);

        /// <summary>The bytes <paramref name="caller"/>'s live sessions hold.</summary>
        public long UsedBy(string caller)
        {
            lock (_callersGate) return _byCaller.GetValueOrDefault(caller);
        }

        /// <summary>
        /// Claims <paramref name="bytes"/> for <paramref name="caller"/> if they fit both the caller's share and
        /// the whole budget; nothing is claimed otherwise. Release them with <see cref="Release(long, string)"/>.
        /// </summary>
        public SessionReservation TryReserve(long bytes, string caller)
        {
            lock (_callersGate)
            {
                var held = _byCaller.GetValueOrDefault(caller);
                if (held + bytes > _maxBytesPerCaller) return SessionReservation.OverCallerShare;
                if (!TryReserve(bytes)) return SessionReservation.OverTotal;
                _byCaller[caller] = held + bytes;
                return SessionReservation.Reserved;
            }
        }

        public void Release(long bytes, string caller)
        {
            lock (_callersGate)
            {
                var left = _byCaller.GetValueOrDefault(caller) - bytes;
                if (left > 0) _byCaller[caller] = left;
                else _byCaller.Remove(caller);
            }
            Release(bytes);
        }
    }

    /// <summary>What <see cref="SessionUploadBudget.TryReserve(long, string)"/> came to.</summary>
    public enum SessionReservation
    {
        Reserved,

        /// <summary>The caller's sessions already hold their share.</summary>
        OverCallerShare,

        /// <summary>Every session together holds the whole budget.</summary>
        OverTotal,
    }
}
