using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.ExternalApi;
using Microsoft.EntityFrameworkCore;
using TWStockLib.Models;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>What a caller needs of one stock's history.</summary>
    /// <param name="Symbol">TW stock code.</param>
    /// <param name="NeedFrom">The earliest day a price is needed for.</param>
    /// <param name="HeldFrom">The first day the stock is known to have been trading (the day the user
    /// first held it), or null when nothing is known — a benchmark. An empty month on or after it can
    /// only be a fetch that went wrong, never "not listed yet".</param>
    public sealed record HistoryWant(string Symbol, DateOnly NeedFrom, DateOnly? HeldFrom);

    public enum BackfillState
    {
        /// <summary>Everything asked for is stored.</summary>
        Complete,

        /// <summary>Months are still being fetched in the background.</summary>
        Running,

        /// <summary>Something is missing and nothing is working on it — a month kept failing, or the
        /// queue is full. Asking again after a while tries again.</summary>
        Stalled,
    }

    /// <param name="State">Whether everything asked for is stored, is being fetched, or is stuck.</param>
    /// <param name="CoveredFrom">The earliest day every stock asked about has prices from.</param>
    /// <param name="TargetFrom">The earliest day that was asked for.</param>
    public sealed record BackfillStatus(BackfillState State, DateOnly CoveredFrom, DateOnly TargetFrom);

    public interface ITwHistoryBackfill
    {
        /// <summary>
        /// Queues whatever months of the given stocks are not stored yet and says how far along that
        /// is. Returns at once — the fetching happens in the background, a month at a time, behind
        /// whatever a person is waiting for. Stocks whose deep fetch has not run yet are left out:
        /// there is no recent window to extend.
        /// </summary>
        Task<BackfillStatus> RequestAsync(IReadOnlyList<HistoryWant> wants, DateOnly targetFrom, CancellationToken ct);
    }

    /// <summary>
    /// Fills in the older price history of TW stocks (what a 1- or 3-year chart needs beyond the ~6
    /// months <see cref="PersistentTwHistoricalProvider"/> keeps) without a person waiting on it and
    /// without hammering the exchanges: one month at a time, every month through the
    /// <see cref="IThirdPartyGateway"/> as <see cref="GatewayPriority.Background"/>, and each month
    /// is stored the moment it arrives, so progress survives a restart and the chart can already use
    /// what has been fetched.
    ///
    /// The work is ordered month-major, newest first, across all stocks: the stock whose next missing
    /// month is the newest goes next. A chart only shows days on which every stock it holds has a
    /// price, so what it can display grows leftwards as each month is finished for all of them —
    /// finishing one stock's whole history first would leave it showing nothing for much longer.
    ///
    /// Coverage is tracked per stock as <see cref="StockSymbolInfo.HistoryCoveredFrom"/>. The queue
    /// itself is in memory: a restart loses it, and the next request that needs the history puts it
    /// back, picking up from the stored coverage.
    /// </summary>
    public sealed class TwHistoryBackfillService(
        IServiceScopeFactory scopes,
        IDbContextFactory<AzureSQLDBContext> dbFactory,
        ILogger<TwHistoryBackfillService> logger,
        TimeProvider? timeProvider = null) : BackgroundService, ITwHistoryBackfill
    {
        /// <summary>A month that keeps failing is given up on, for now, after this many tries.</summary>
        internal const int MaxFailures = 3;

        /// <summary>After giving up on a stock, requests for it are not queued again for this long, so
        /// a page that polls cannot turn one broken symbol into a stream of retries.</summary>
        internal static readonly TimeSpan GiveUpFor = TimeSpan.FromMinutes(10);

        /// <summary>Breathing room after a failed month, on top of the gateway's own pacing.</summary>
        internal static readonly TimeSpan PauseAfterFailure = TimeSpan.FromSeconds(5);

        /// <summary>The queue is for a person's own holdings plus a few benchmarks; this only stops a
        /// request from queueing an unbounded number of symbols.</summary>
        internal const int MaxQueuedSymbols = 100;

        /// <summary>Empty months in a row, with nothing known about when the stock listed, before
        /// everything older is taken to be before its listing.</summary>
        private const int EmptyMonthsBeforeListing = 2;

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
        private readonly object _gate = new();
        private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTimeOffset> _giveUps = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _wake = new(0);

        internal enum StepResult { Idle, Progressed, Failed }

        private sealed class Job(string symbol, DateOnly needFrom, DateOnly? heldFrom, DateOnly coveredFrom)
        {
            public string Symbol { get; } = symbol;
            public DateOnly NeedFrom { get; set; } = needFrom;
            public DateOnly? HeldFrom { get; set; } = heldFrom;
            public DateOnly CoveredFrom { get; set; } = coveredFrom;
            public MarketType? Market { get; set; }
            public int Failures { get; set; }
            public int EmptyMonths { get; set; }

            // Only the worker moves CoveredFrom; a request can lower NeedFrom/HeldFrom at any time,
            // so those two are read and written under the service's lock.
            public DateOnly NextMonth => CoveredFrom.AddMonths(-1);
        }

        internal int QueuedSymbols
        {
            get { lock (_gate) return _jobs.Count; }
        }

        public async Task<BackfillStatus> RequestAsync(
            IReadOnlyList<HistoryWant> wants, DateOnly targetFrom, CancellationToken ct)
        {
            var symbols = wants.Select(w => w.Symbol).Distinct().ToList();
            Dictionary<string, StockSymbolInfo> infos;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                infos = await db.StockSymbolInfos
                    .AsNoTracking()
                    .Where(i => symbols.Contains(i.Symbol))
                    .ToDictionaryAsync(i => i.Symbol, ct);
            }

            var now = _time.GetUtcNow();
            var running = false;
            var stalled = false;
            var queued = false;
            DateOnly? coveredFrom = null;

            foreach (var want in wants)
            {
                if (!infos.TryGetValue(want.Symbol, out var info) || CoveredFromOf(info) is not { } covered)
                    continue;
                if (covered <= MonthStart(want.NeedFrom)) continue;

                // The oldest day of any incomplete stock limits what a chart can show.
                coveredFrom = coveredFrom is null || covered > coveredFrom ? covered : coveredFrom;

                lock (_gate)
                {
                    if (_jobs.TryGetValue(want.Symbol, out var job))
                    {
                        // The same stock asked for again, perhaps by someone who wants more of it.
                        if (want.NeedFrom < job.NeedFrom) job.NeedFrom = want.NeedFrom;
                        if (want.HeldFrom is { } held && (job.HeldFrom is not { } had || held < had)) job.HeldFrom = held;
                        running = true;
                    }
                    else if (_giveUps.TryGetValue(want.Symbol, out var until) && until > now)
                    {
                        stalled = true;
                    }
                    else if (_jobs.Count >= MaxQueuedSymbols)
                    {
                        logger.LogWarning("Backfill queue is full ({Count}); not queueing {Symbol}", _jobs.Count, want.Symbol);
                        stalled = true;
                    }
                    else
                    {
                        _jobs[want.Symbol] = new Job(want.Symbol, want.NeedFrom, want.HeldFrom, covered)
                        {
                            Market = Enum.TryParse<MarketType>(info.TwMarketType, out var market) ? market : null,
                        };
                        running = true;
                        queued = true;
                    }
                }
            }

            if (queued) _wake.Release();

            var state = running ? BackfillState.Running : stalled ? BackfillState.Stalled : BackfillState.Complete;
            var reached = state == BackfillState.Complete || coveredFrom is null
                ? targetFrom
                : coveredFrom.Value > targetFrom ? coveredFrom.Value : targetFrom;
            return new BackfillStatus(state, reached, targetFrom);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                StepResult step;
                try
                {
                    step = await StepAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // One bad step must not end the loop for the life of the process.
                    logger.LogError(ex, "History backfill step threw");
                    step = StepResult.Failed;
                }

                try
                {
                    if (step == StepResult.Idle) await _wake.WaitAsync(stoppingToken);
                    else if (step == StepResult.Failed) await Task.Delay(PauseAfterFailure, _time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>Fetches and stores one month for whichever stock is next in line.</summary>
        internal async Task<StepResult> StepAsync(CancellationToken ct)
        {
            Job? job;
            DateOnly needFrom;
            DateOnly? heldFrom;
            lock (_gate)
            {
                // Newest missing month first, across every stock (see the class remarks).
                job = _jobs.Values
                    .OrderByDescending(j => j.NextMonth)
                    .ThenBy(j => j.Symbol, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (job is null) return StepResult.Idle;
                needFrom = job.NeedFrom;
                heldFrom = job.HeldFrom;
            }

            var month = job.NextMonth;
            await using var scope = scopes.CreateAsyncScope();
            var history = scope.ServiceProvider.GetRequiredService<ITWStockLibHistoricalProvider>();

            job.Market ??= await history.DetermineMarketType(job.Symbol);
            if (job.Market is not { } market)
            {
                logger.LogWarning("Backfill: market of {Symbol} unresolved", job.Symbol);
                return Fail(job);
            }

            var fetched = await history.FetchMonthAsync(job.Symbol, month, market, GatewayPriority.Background, ct);
            if (!fetched.Succeeded) return Fail(job);

            if (fetched.Bars.Count > 0)
            {
                job.EmptyMonths = 0;
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await DailyStockPriceWriter.UpsertFinalizedAsync(
                    db, job.Symbol, fetched.Bars, DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime), logger, ct);
                await AdvanceCoverageAsync(job, month, ct);
                return StepResult.Progressed;
            }

            // An empty month. A stock the user held that day was trading, so there it can only be a
            // response that failed to say so (the exchange answers "no data" for plenty of reasons
            // other than there being none) — and writing it down as covered would leave a hole
            // nothing ever looks at again.
            if (heldFrom is { } held && month >= MonthStart(held)) return Fail(job);

            job.EmptyMonths++;
            await AdvanceCoverageAsync(job, month, ct);
            if (job.EmptyMonths >= (heldFrom is null ? EmptyMonthsBeforeListing : 1))
            {
                // Before the listing there is nothing older to find. Say the rest is covered.
                logger.LogInformation("Backfill: {Symbol} has nothing before {Month:yyyy-MM}; done", job.Symbol, month);
                await AdvanceCoverageAsync(job, MonthStart(needFrom), ct);
            }
            return StepResult.Progressed;
        }

        private StepResult Fail(Job job)
        {
            job.Failures++;
            if (job.Failures < MaxFailures) return StepResult.Failed;

            logger.LogWarning(
                "Backfill: giving up on {Symbol} at {Month:yyyy-MM} after {Failures} failures; retry in {Cooldown}",
                job.Symbol, job.NextMonth, job.Failures, GiveUpFor);
            lock (_gate)
            {
                _jobs.Remove(job.Symbol);
                _giveUps[job.Symbol] = _time.GetUtcNow() + GiveUpFor;
            }
            return StepResult.Failed;
        }

        private async Task AdvanceCoverageAsync(Job job, DateOnly month, CancellationToken ct)
        {
            job.Failures = 0;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                var info = await db.StockSymbolInfos.FindAsync([job.Symbol], ct);
                if (info is not null && (CoveredFromOf(info) is not { } now || now > month))
                {
                    info.HistoryCoveredFrom = month;
                    await db.SaveChangesAsync(ct);
                }
            }

            bool done;
            lock (_gate)
            {
                if (month < job.CoveredFrom) job.CoveredFrom = month;
                // NeedFrom is read now, not as the step began: a request may have asked for more
                // while this month was in flight, and then the job carries on.
                done = job.CoveredFrom <= MonthStart(job.NeedFrom);
                if (done) _jobs.Remove(job.Symbol);
            }
            if (done) logger.LogInformation("Backfill: {Symbol} stored back to {Month:yyyy-MM}", job.Symbol, job.CoveredFrom);
        }

        /// <summary>The first day from which the stored history of a stock is whole: what the
        /// backfill has reached, or, before it has run, what the deep fetch's window covered. Null
        /// when neither has happened — there is no recent window to extend yet.</summary>
        internal static DateOnly? CoveredFromOf(StockSymbolInfo info)
        {
            if (info.HistoryCoveredFrom is { } stored) return stored;
            if (info.DeepFetchedAt is not { } deepAt) return null;

            // The deep fetch asked for six months back from the day it ran, from the middle of a
            // month; the first month that window holds in full is the one after.
            var windowStart = DateOnly.FromDateTime(deepAt.AddMonths(-PersistentTwHistoricalProvider.LookbackMonths));
            return windowStart.Day == 1 ? windowStart : MonthStart(windowStart).AddMonths(1);
        }

        internal static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);
    }
}
