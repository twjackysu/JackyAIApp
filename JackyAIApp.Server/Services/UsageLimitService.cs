using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// Backs <see cref="IUsageLimitService"/> with a single atomic conditional UPDATE against
    /// <c>DailyUsages</c> (no read-modify-write round trip), so two concurrent requests can never
    /// jointly overspend one user's daily budget. See ARCHITECTURE.md "Credits & Payments 移除設計".
    /// Every charge it takes goes into the request's <see cref="UsageLedger"/>, which decides at the
    /// end of the request whether it is refunded.
    /// </summary>
    public class UsageLimitService(
        AzureSQLDBContext dbContext,
        IOptions<UsageLimitOptions> options,
        UsageLedger ledger,
        ILogger<UsageLimitService> logger,
        TimeProvider? timeProvider = null) : IUsageLimitService
    {
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly UsageLimitOptions _options = options.Value;
        private readonly ILogger<UsageLimitService> _logger = logger;
        private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

        public async Task<bool> TryConsumeAsync(string userId, ulong cost, string feature)
        {
            var charged = await TryChargeAsync(userId, cost, feature);
            if (charged)
                ledger.RecordCharge(userId, cost, feature);
            return charged;
        }

        private async Task<bool> TryChargeAsync(string userId, ulong cost, string feature)
        {
            // Usage weights (UsageCosts) are tiny constants — narrowing to long is always exact
            // and lets the atomic UPDATE below compare against a native bigint column.
            var costLong = checked((long)cost);
            var limit = await GetEffectiveLimitAsync(userId);

            if (costLong > limit)
            {
                _logger.LogInformation(
                    "Usage limit: {UserId} blocked for {Feature} — cost {Cost} exceeds the entire daily limit {Limit}",
                    userId, feature, costLong, limit);
                return false;
            }

            var today = _timeProvider.GetUtcNow().UtcDateTime.Date;

            if (await TryAtomicIncrementAsync(userId, today, costLong, limit))
                return true;

            // Either today's row doesn't exist yet (first charge of the day) or it's already at
            // the limit. Try to create it — guarded by a NOT EXISTS check so a concurrent request
            // doing the same thing can only win the race once.
            try
            {
                var inserted = await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO DailyUsages (UserId, [Date], Used)
SELECT {userId}, {today}, {costLong}
WHERE NOT EXISTS (SELECT 1 FROM DailyUsages WHERE UserId = {userId} AND [Date] = {today})");

                if (inserted > 0)
                    return true;
            }
            catch (Exception ex)
            {
                // Lost the insert race to a concurrent request (PK violation on (UserId, Date)) —
                // fall through and retry the conditional update against the row it just created.
                // Caught broadly (not DbUpdateException) because ExecuteSqlInterpolatedAsync runs
                // outside SaveChanges and raises the provider's own exception type directly
                // (SqlException / SqliteException), not EF's wrapped one.
                _logger.LogDebug(ex, "Usage limit: insert-if-missing lost a race for {UserId}, retrying update", userId);
            }

            // The row exists now; this is the one retry needed to cover the race above. If it
            // still fails, the user is genuinely out of room today.
            return await TryAtomicIncrementAsync(userId, today, costLong, limit);
        }

        public async Task RefundAsync(string userId, ulong cost, string feature)
        {
            var costLong = checked((long)cost);
            var today = _timeProvider.GetUtcNow().UtcDateTime.Date;

            // Clamp at zero: a refund must never drive Used negative, no matter how many times
            // it's called (e.g. a double-refund) or how small today's row's balance already is.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE DailyUsages SET Used = CASE WHEN Used > {costLong} THEN Used - {costLong} ELSE 0 END
WHERE UserId = {userId} AND [Date] = {today}");

            _logger.LogInformation("Usage limit: refunded {Cost} to {UserId} for {Feature}", costLong, userId, feature);
        }

        private async Task<bool> TryAtomicIncrementAsync(string userId, DateTime date, long cost, long limit)
        {
            var affected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE DailyUsages SET Used = Used + {cost}
WHERE UserId = {userId} AND [Date] = {date} AND Used + {cost} <= {limit}");
            return affected > 0;
        }

        private async Task<long> GetEffectiveLimitAsync(string userId)
        {
            var multiplier = await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => (decimal?)u.UsageLimitMultiplier)
                .FirstOrDefaultAsync() ?? 1m;

            return (long)(_options.DailyBudget * multiplier);
        }
    }
}
