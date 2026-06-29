using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// Runs daily after US market close (23:00 UTC) to store per-user net worth snapshots.
    /// Uses IServiceScopeFactory because scoped services can't be injected into singletons.
    /// </summary>
    public class PortfolioSnapshotJob(
        IServiceScopeFactory scopeFactory,
        ILogger<PortfolioSnapshotJob> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("PortfolioSnapshotJob started");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                // Run at 23:00 UTC daily (US markets close 21:00 UTC; give 2h buffer)
                var nextRun = new DateTime(now.Year, now.Month, now.Day, 23, 0, 0, DateTimeKind.Utc);
                if (now >= nextRun) nextRun = nextRun.AddDays(1);

                var delay = nextRun - now;
                logger.LogInformation("PortfolioSnapshotJob next run in {delay}", delay);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                await RunSnapshotsAsync(stoppingToken);
            }
        }

        private async Task RunSnapshotsAsync(CancellationToken ct)
        {
            logger.LogInformation("PortfolioSnapshotJob running daily snapshots");

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AzureSQLDBContext>();
            var portfolioService = scope.ServiceProvider.GetRequiredService<IPortfolioService>();

            // Find all users with at least one transaction
            var userIds = await db.PortfolioTransactions
                .Select(t => t.UserId)
                .Distinct()
                .ToListAsync(ct);

            logger.LogInformation("Taking snapshots for {Count} users", userIds.Count);

            foreach (var userId in userIds)
            {
                try
                {
                    await portfolioService.TakeSnapshotAsync(userId, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Snapshot failed for user {UserId}", userId);
                }
            }

            logger.LogInformation("PortfolioSnapshotJob daily snapshots complete");
        }
    }
}
