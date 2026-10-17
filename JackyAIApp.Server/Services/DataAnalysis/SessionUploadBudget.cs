using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// The estimated bytes every live upload session holds, against
    /// <see cref="SessionUploadOptions.MaxTotalBytes"/>. Kept here rather than as a SizeLimit on the shared
    /// IMemoryCache, which would make every other cache entry in the app declare a Size.
    /// A session reserves its bytes before it is stored and releases them from its cache entry's
    /// post-eviction callback (expiry or removal).
    /// </summary>
    public sealed class SessionUploadBudget(IOptions<SessionUploadOptions> options)
    {
        private readonly long _maxBytes = options.Value.MaxTotalBytes;
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
    }
}
