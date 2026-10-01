using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// UsageLimitService's atomic conditional-UPDATE/insert-if-missing logic relies on raw SQL
    /// (<c>ExecuteSqlInterpolatedAsync</c>), which the InMemory EF provider does not support at
    /// all (throws immediately) — so these tests run against a real relational provider instead.
    /// SQLite's shared-cache in-memory mode gives that without needing SQL Server/Docker in CI,
    /// and — unlike InMemory — actually exercises the race the design is meant to prevent when
    /// multiple DbContext instances hit the same connection concurrently. See
    /// DailyUsage.Used's HasConversion/HasColumnType("bigint") comment in AzureSQLDBContext for
    /// why the column is bigint rather than a decimal(20,0): SQLite stores `decimal` as TEXT,
    /// which the raw SQL in this service would compare lexicographically rather than numerically.
    /// </summary>
    public class UsageLimitServiceTests : IDisposable
    {
        // A named shared-cache in-memory database (not the default private ":memory:") so that
        // separate SqliteConnection instances — one per DbContext, as the concurrency test below
        // needs for genuine parallel access — all see the same database. SQLite drops a
        // shared-cache in-memory database once its last connection closes, so `_keepAliveConnection`
        // stays open for the lifetime of each test purely to keep it alive; it is never used
        // directly to run a command.
        private readonly SqliteConnection _keepAliveConnection;
        private readonly DbContextOptions<AzureSQLDBContext> _options;
        private const string TestUserId = "usage-limit-test-user";

        public UsageLimitServiceTests()
        {
            var connectionString = $"Data Source=file:usagelimit-{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keepAliveConnection = new SqliteConnection(connectionString);
            _keepAliveConnection.Open();

            _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseSqlite(connectionString)
                .Options;

            using var context = new AzureSQLDBContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _keepAliveConnection.Dispose();
        }

        private AzureSQLDBContext CreateContext() => new(_options);

        private async Task SeedUserAsync(decimal multiplier = 1m)
        {
            using var context = CreateContext();
            context.Users.Add(new User
            {
                Id = TestUserId,
                LastUpdated = DateTime.UtcNow,
                UsageLimitMultiplier = multiplier,
            });
            await context.SaveChangesAsync();
        }

        private static UsageLimitService BuildService(
            AzureSQLDBContext context, ulong dailyBudget = 10, TimeProvider? timeProvider = null) =>
            new(context,
                Options.Create(new UsageLimitOptions { DailyBudget = dailyBudget }),
                NullLogger<UsageLimitService>.Instance,
                timeProvider);

        private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        [Fact]
        public async Task TryConsumeAsync_WithinBudget_Succeeds()
        {
            await SeedUserAsync();
            using var context = CreateContext();
            var service = BuildService(context, dailyBudget: 10);

            var result = await service.TryConsumeAsync(TestUserId, 3, "test_feature");

            Assert.True(result);
        }

        [Fact]
        public async Task TryConsumeAsync_ExceedsBudget_Blocked()
        {
            await SeedUserAsync();
            using var context = CreateContext();
            var service = BuildService(context, dailyBudget: 10);

            Assert.True(await service.TryConsumeAsync(TestUserId, 7, "test_feature"));
            // 7 + 5 = 12 > 10
            var blocked = await service.TryConsumeAsync(TestUserId, 5, "test_feature");

            Assert.False(blocked);
        }

        [Fact]
        public async Task TryConsumeAsync_SingleChargeLargerThanWholeBudget_Blocked()
        {
            await SeedUserAsync();
            using var context = CreateContext();
            var service = BuildService(context, dailyBudget: 10);

            var result = await service.TryConsumeAsync(TestUserId, 11, "test_feature");

            Assert.False(result);
        }

        [Fact]
        public async Task TryConsumeAsync_ExactlyAtBudget_Succeeds()
        {
            await SeedUserAsync();
            using var context = CreateContext();
            var service = BuildService(context, dailyBudget: 10);

            Assert.True(await service.TryConsumeAsync(TestUserId, 6, "test_feature"));
            // 6 + 4 = 10, exactly the budget — must still pass ("<=", not "<").
            Assert.True(await service.TryConsumeAsync(TestUserId, 4, "test_feature"));
        }

        [Fact]
        public async Task TryConsumeAsync_CrossDay_ResetsBudget()
        {
            await SeedUserAsync();
            var day1 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var day2 = new DateTimeOffset(2026, 1, 2, 0, 0, 1, TimeSpan.Zero);

            using (var context = CreateContext())
            {
                var service = BuildService(context, dailyBudget: 10, new FakeTimeProvider(day1));
                Assert.True(await service.TryConsumeAsync(TestUserId, 10, "test_feature"));
                Assert.False(await service.TryConsumeAsync(TestUserId, 1, "test_feature"));
            }

            using (var context = CreateContext())
            {
                var service = BuildService(context, dailyBudget: 10, new FakeTimeProvider(day2));
                // A new UTC day is an entirely separate budget, even though it's only seconds later.
                var result = await service.TryConsumeAsync(TestUserId, 10, "test_feature");
                Assert.True(result);
            }
        }

        [Fact]
        public async Task TryConsumeAsync_Multiplier_RaisesEffectiveLimit()
        {
            await SeedUserAsync(multiplier: 3m);
            using var context = CreateContext();
            var service = BuildService(context, dailyBudget: 10);

            // Default budget alone (10) could not cover this; the x3 multiplier (30) can.
            var result = await service.TryConsumeAsync(TestUserId, 25, "test_feature");

            Assert.True(result);
        }

        [Fact]
        public async Task TryConsumeAsync_UnknownUser_FailsGracefullyWithoutThrowing()
        {
            // No Users row at all for this id. GetEffectiveLimitAsync's own "?? 1m" fallback
            // would otherwise grant the default budget, but DailyUsage.UserId has a FK to
            // Users.Id (every real caller already has an authenticated, persisted user, so this
            // never happens in practice) — so the insert is correctly rejected by the database
            // instead of silently tracking usage against a nonexistent user. The one contract
            // that matters here is that this is a clean `false`, not an unhandled exception.
            using var context = CreateContext();
            var service = BuildService(context, dailyBudget: 10);

            var result = await service.TryConsumeAsync("no-such-user", 5, "test_feature");

            Assert.False(result);
        }

        [Fact]
        public async Task RefundAsync_ReducesUsed()
        {
            await SeedUserAsync();
            using var context = CreateContext();
            var service = BuildService(context, dailyBudget: 10);
            await service.TryConsumeAsync(TestUserId, 8, "test_feature");

            await service.RefundAsync(TestUserId, 3, "test_feature");

            // 8 - 3 = 5 refunded back, so another 5 should now fit exactly at the limit (10).
            var result = await service.TryConsumeAsync(TestUserId, 5, "test_feature");
            Assert.True(result);
        }

        [Fact]
        public async Task RefundAsync_NeverDrivesUsedNegative()
        {
            await SeedUserAsync();
            using var context = CreateContext();
            var service = BuildService(context, dailyBudget: 10);
            await service.TryConsumeAsync(TestUserId, 2, "test_feature");

            // Refund far more than was ever charged (e.g. a double-refund bug elsewhere).
            await service.RefundAsync(TestUserId, 100, "test_feature");

            // If Used had gone negative (and the ulong column wrapped), this would incorrectly
            // read as already far over budget and block below. Used must have clamped at 0.
            var result = await service.TryConsumeAsync(TestUserId, 10, "test_feature");
            Assert.True(result);
        }

        [Fact]
        public async Task RefundAsync_NoRowForToday_DoesNotThrowOrCreateNegativeRow()
        {
            await SeedUserAsync();
            using var context = CreateContext();
            var service = BuildService(context, dailyBudget: 10);

            // Nothing was ever charged today — refunding should be a safe no-op.
            await service.RefundAsync(TestUserId, 5, "test_feature");

            var result = await service.TryConsumeAsync(TestUserId, 10, "test_feature");
            Assert.True(result);
        }

        [Fact]
        public async Task TryConsumeAsync_ConcurrentRequests_NeverOversells()
        {
            await SeedUserAsync();
            const int budget = 5;
            const int requesters = 10; // each spends 1 — only `budget` of these may succeed

            var tasks = Enumerable.Range(0, requesters).Select(_ => Task.Run(async () =>
            {
                using var context = CreateContext();
                var service = BuildService(context, dailyBudget: budget);
                return await service.TryConsumeAsync(TestUserId, 1, "concurrent_test");
            }));

            var results = await Task.WhenAll(tasks);

            Assert.Equal(budget, results.Count(r => r));

            using var verifyContext = CreateContext();
            var totalUsed = await verifyContext.DailyUsages.SingleAsync(d => d.UserId == TestUserId);
            Assert.Equal((ulong)budget, totalUsed.Used);
        }
    }
}
