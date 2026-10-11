using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    public class TaiexTotalReturnStoreTests
    {
        private readonly DbContextOptions<AzureSQLDBContext> _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        private TaiexTotalReturnStore CreateStore() => new(new TestDbContextFactory(_options));

        private static IndexClose Day(int month, int day, decimal close) => new(new DateOnly(2026, month, day), close);

        [Fact]
        public async Task GetRangeAsync_EmptyTable_IsNull() =>
            Assert.Null(await CreateStore().GetRangeAsync());

        [Fact]
        public async Task GetRangeAsync_ReportsEarliestLatestAndCount()
        {
            var store = CreateStore();
            await store.UpsertAsync([Day(9, 3, 3), Day(8, 1, 1), Day(9, 1, 2)]);

            var range = await store.GetRangeAsync();

            Assert.Equal(new TaiexStoredRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 3), 3), range);
        }

        [Fact]
        public async Task GetAllAsync_IsAscendingByDate()
        {
            var store = CreateStore();
            await store.UpsertAsync([Day(9, 3, 3), Day(8, 1, 1), Day(9, 1, 2)]);

            Assert.Equal([1m, 2m, 3m], (await store.GetAllAsync()).Select(c => c.Close));
        }

        [Fact]
        public async Task UpsertAsync_StoringTheSameDaysAgain_ChangesNothing()
        {
            var store = CreateStore();
            var days = new[] { Day(9, 1, 100m), Day(9, 2, 101.5m) };

            Assert.Equal(2, await store.UpsertAsync(days));
            Assert.Equal(0, await store.UpsertAsync(days));

            Assert.Equal(2, (await store.GetRangeAsync())!.Value.Count);
        }

        [Fact]
        public async Task UpsertAsync_ADifferentCloseForAStoredDay_Overwrites()
        {
            var store = CreateStore();
            await store.UpsertAsync([Day(9, 1, 100m), Day(9, 2, 101m)]);

            Assert.Equal(1, await store.UpsertAsync([Day(9, 2, 102m)]));

            Assert.Equal([100m, 102m], (await store.GetAllAsync()).Select(c => c.Close));
        }

        [Fact]
        public async Task UpsertAsync_RepeatedDateInTheInput_StoresOneRow_LastWins()
        {
            var store = CreateStore();

            await store.UpsertAsync([Day(9, 1, 100m), Day(9, 1, 105m)]);

            Assert.Equal(105m, Assert.Single(await store.GetAllAsync()).Close);
        }

        [Fact]
        public async Task UpsertAsync_Nothing_IsANoOp() =>
            Assert.Equal(0, await CreateStore().UpsertAsync([]));
    }
}
