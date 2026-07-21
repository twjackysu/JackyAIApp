using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace JackyAIApp.Server.Tests.Services
{
    public class CreditServiceTests : IDisposable
    {
        private readonly AzureSQLDBContext _dbContext;
        private readonly CreditService _service;
        private const string UserId = "user-1";

        public CreditServiceTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                // CreditService uses explicit transactions; InMemory ignores them.
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            _dbContext = new AzureSQLDBContext(options);
            _service = new CreditService(_dbContext, NullLogger<CreditService>.Instance);

            _dbContext.Users.Add(new User
            {
                Id = UserId,
                Name = "U",
                Email = "u@example.com",
                CreditBalance = 0,
                TotalCreditsUsed = 0,
                LastUpdated = DateTime.UtcNow
            });
            _dbContext.SaveChanges();
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task AddCredits_SameReferenceId_IsAppliedOnlyOnce()
        {
            var ok1 = await _service.AddCreditsAsync(UserId, 500, "purchase", "ECPay", referenceId: "TRADE-123");
            var ok2 = await _service.AddCreditsAsync(UserId, 500, "purchase", "ECPay replay", referenceId: "TRADE-123");

            Assert.True(ok1);
            Assert.True(ok2); // idempotent success, not an error
            Assert.Equal(500UL, await _service.GetBalanceAsync(UserId));
            Assert.Equal(1, await _dbContext.CreditTransactions.CountAsync(t => t.ReferenceId == "TRADE-123"));
        }

        [Fact]
        public async Task AddCredits_NoReferenceId_AppliesEveryTime()
        {
            await _service.AddCreditsAsync(UserId, 10, "bonus", "daily");
            await _service.AddCreditsAsync(UserId, 10, "bonus", "daily");

            Assert.Equal(20UL, await _service.GetBalanceAsync(UserId));
        }

        [Fact]
        public async Task AddCredits_DifferentReferenceIds_BothApplied()
        {
            await _service.AddCreditsAsync(UserId, 500, "purchase", "p1", referenceId: "A");
            await _service.AddCreditsAsync(UserId, 300, "purchase", "p2", referenceId: "B");

            Assert.Equal(800UL, await _service.GetBalanceAsync(UserId));
        }

        [Fact]
        public async Task GrantMonthlyQuota_ResetsRatherThanAdds()
        {
            await _service.AddCreditsAsync(UserId, 500, "bonus", "leftover from free tier");

            var ok = await _service.GrantMonthlyQuotaAsync(UserId, 3000, "ecpay-period-1", "subscription");

            Assert.True(ok);
            // 3000, not 3500 — a period grant resets the balance, it doesn't top it up.
            Assert.Equal(3000UL, await _service.GetBalanceAsync(UserId));
        }

        [Fact]
        public async Task GrantMonthlyQuota_SameReferenceId_DoesNotResetAgain()
        {
            await _service.GrantMonthlyQuotaAsync(UserId, 3000, "ecpay-period-1", "subscription");
            await _service.ConsumeCreditsAsync(UserId, 1000, "cloze_test"); // user spends some of this period's quota

            var replay = await _service.GrantMonthlyQuotaAsync(UserId, 3000, "ecpay-period-1", "subscription (replayed callback)");

            Assert.True(replay); // idempotent success, not an error
            // Must stay at 2000 (3000 - 1000 spent) — a replayed callback for a charge already
            // granted must never reset the balance back to the full quota mid-period.
            Assert.Equal(2000UL, await _service.GetBalanceAsync(UserId));
        }

        [Fact]
        public async Task GrantMonthlyQuota_DifferentReferenceIds_EachResets()
        {
            await _service.GrantMonthlyQuotaAsync(UserId, 3000, "ecpay-period-1", "subscription month 1");
            await _service.ConsumeCreditsAsync(UserId, 2900, "cloze_test");

            await _service.GrantMonthlyQuotaAsync(UserId, 3000, "ecpay-period-2", "subscription month 2");

            // New period, new charge, new referenceId — resets to the full quota regardless of
            // how little was left from the previous period.
            Assert.Equal(3000UL, await _service.GetBalanceAsync(UserId));
        }
    }
}
