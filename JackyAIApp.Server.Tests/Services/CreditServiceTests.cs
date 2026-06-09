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
    }
}
