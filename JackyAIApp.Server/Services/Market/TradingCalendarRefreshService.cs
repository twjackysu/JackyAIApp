namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// Keeps <see cref="TwseTradingCalendar"/>'s holiday schedule loaded from outside any request.
    /// Loads at startup, then checks hourly and reloads once the last load is older than
    /// <see cref="ReloadAfter"/> — so a failed load is simply tried again an hour later (the
    /// calendar keeps answering from the schedule it holds, or with "unknown" before the first).
    /// Same shape as <c>TwListedCompanyRefreshService</c>.
    /// </summary>
    public class TradingCalendarRefreshService(
        TwseTradingCalendar calendar,
        ILogger<TradingCalendarRefreshService> logger,
        TimeProvider? timeProvider = null) : BackgroundService
    {
        private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

        /// <summary>The schedule changes a few times a year at most (a typhoon closure is the one
        /// announced at short notice), so twice a day is plenty.</summary>
        internal static readonly TimeSpan ReloadAfter = TimeSpan.FromHours(12);

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
        private DateTimeOffset? _lastLoaded;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(CheckEvery, _time);
            do
            {
                await ReloadIfStaleAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        internal async Task ReloadIfStaleAsync(CancellationToken ct)
        {
            var now = _time.GetUtcNow();
            if (_lastLoaded is { } last && now - last < ReloadAfter) return;
            try
            {
                if (await calendar.RefreshAsync(ct))
                    _lastLoaded = now;
                else
                    logger.LogWarning("TWSE holiday schedule could not be loaded; retrying in {Interval}", CheckEvery);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Never let one bad hour end the loop for the life of the process.
                logger.LogWarning(ex, "TWSE holiday schedule load threw; retrying in {Interval}", CheckEvery);
            }
        }
    }
}
