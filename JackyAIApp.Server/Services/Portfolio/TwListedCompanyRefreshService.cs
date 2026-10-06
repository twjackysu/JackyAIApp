namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// Keeps the TW listed-company list current from outside any request, so a search never waits
    /// on (or fails with) an upstream fetch. Checks at startup and then hourly, and refreshes when
    /// the last successful refresh is older than <see cref="RefreshAfter"/>. A failed refresh is
    /// simply tried again an hour later; search keeps serving the list it has meanwhile.
    ///
    /// Checking hourly rather than sleeping a day between refreshes is what makes this survive
    /// restarts, sleeping App Service instances and failed attempts without any bookkeeping: the
    /// stored refresh time is the only state.
    /// </summary>
    public class TwListedCompanyRefreshService(
        ITwListedCompanyService companies,
        ILogger<TwListedCompanyRefreshService> logger,
        TimeProvider? timeProvider = null) : BackgroundService
    {
        private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

        /// <summary>A new listing shows up in search within about this long of first trading. Each
        /// refresh is two requests of a few seconds, so there is no reason to wait longer.</summary>
        internal static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(12);

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(CheckEvery, _time);
            do
            {
                await RefreshIfStaleAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        internal async Task RefreshIfStaleAsync(CancellationToken ct)
        {
            try
            {
                var last = await companies.GetLastRefreshedAtAsync(ct);
                if (!IsStale(last, _time.GetUtcNow().UtcDateTime)) return;

                if (!await companies.RefreshAsync(ct))
                    logger.LogWarning("TW listed-company refresh failed; serving the existing list, retrying in {Interval}", CheckEvery);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Never let one bad hour (database blip included) end the loop for the life of
                // the process.
                logger.LogWarning(ex, "TW listed-company refresh check threw; retrying in {Interval}", CheckEvery);
            }
        }

        internal static bool IsStale(DateTime? lastRefreshedUtc, DateTime nowUtc) =>
            lastRefreshedUtc is not { } last || nowUtc - last >= RefreshAfter;
    }
}
