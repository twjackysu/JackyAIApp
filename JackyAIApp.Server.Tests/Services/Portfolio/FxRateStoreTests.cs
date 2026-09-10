using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.Portfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// FxRateStore is what both FX sources write through, and the sources are third-party feeds —
    /// whatever shape they hand over has to survive being persisted.
    ///
    /// Note on the provider: EF's InMemory database does not enforce unique indexes, so these tests
    /// pin the de-duplication itself rather than reproducing the SQL Server exception it prevents.
    /// Against the previous implementation the duplicate case inserts two rows here; in production
    /// it instead violated the index, and the resulting DbUpdateException was caught and logged at
    /// debug level, discarding the entire batch.
    /// </summary>
    public class FxRateStoreTests : IDisposable
    {
        private const string Source = FxRateSources.Cathay;
        private static readonly DateOnly Day = new(2026, 9, 3);

        private readonly AzureSQLDBContext _db;
        private readonly FxRateStore _store;

        public FxRateStoreTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _db = new AzureSQLDBContext(options);
            _store = new FxRateStore(_db, Source, NullLogger.Instance);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task AddMissing_BatchRepeatsTheSameDateAndCurrency_StoresItOnce()
        {
            await _store.AddMissingAsync(
                [
                    new(Day, "USD", 31.68m, 31.88m),
                    new(Day, "USD", 31.68m, 31.88m),
                ],
                CancellationToken.None);

            var row = Assert.Single(await _db.FxRates.ToListAsync());
            Assert.Equal(31.68m, row.BuyRate);
        }

        [Fact]
        public async Task AddMissing_ADuplicateDoesNotTakeTheRestOfTheBatchWithIt()
        {
            // The failure that made this worth fixing: one repeated key rejected the whole
            // SaveChanges, so the other currencies in the same response were lost too.
            await _store.AddMissingAsync(
                [
                    new(Day, "USD", 31.68m, 31.88m),
                    new(Day, "USD", 31.68m, 31.88m),
                    new(Day, "EUR", 37.10m, 37.40m),
                    new(Day, "JPY", 0.2140m, 0.2180m),
                ],
                CancellationToken.None);

            var stored = await _db.FxRates.OrderBy(r => r.Currency).ToListAsync();
            Assert.Equal(["EUR", "JPY", "USD"], stored.Select(r => r.Currency));
        }

        [Fact]
        public async Task AddMissing_ConflictingValuesForOneKey_KeepsTheFirst()
        {
            // Arbitrary but deterministic: a source contradicting itself within one response is
            // malformed either way, and silently picking the last would depend on iteration order.
            await _store.AddMissingAsync(
                [
                    new(Day, "USD", 31.68m, 31.88m),
                    new(Day, "USD", 99.99m, 99.99m),
                ],
                CancellationToken.None);

            Assert.Equal(31.68m, (await _db.FxRates.SingleAsync()).BuyRate);
        }

        [Fact]
        public async Task AddMissing_SameDateDifferentCurrencies_AreNotTreatedAsDuplicates()
        {
            // The key is the pair; de-duplicating on date alone would drop 13 of 14 currencies.
            await _store.AddMissingAsync(
                [
                    new(Day, "USD", 31.68m, 31.88m),
                    new(Day, "EUR", 37.10m, 37.40m),
                ],
                CancellationToken.None);

            Assert.Equal(2, await _db.FxRates.CountAsync());
        }

        [Fact]
        public async Task AddMissing_SameCurrencyDifferentDates_AreNotTreatedAsDuplicates()
        {
            await _store.AddMissingAsync(
                [
                    new(Day, "USD", 31.68m, 31.88m),
                    new(Day.AddDays(-1), "USD", 31.66m, 31.86m),
                ],
                CancellationToken.None);

            Assert.Equal(2, await _db.FxRates.CountAsync());
        }

        [Fact]
        public async Task AddMissing_AlreadyStored_IsLeftAloneRatherThanOverwritten()
        {
            // A published rate never changes, so an existing row is the authority.
            _db.FxRates.Add(new FxRate
            {
                RateDate = Day, Currency = "USD", Source = Source,
                BuyRate = 31.68m, SellRate = 31.88m,
            });
            await _db.SaveChangesAsync();

            await _store.AddMissingAsync(
                [new(Day, "USD", 99.99m, 99.99m)],
                CancellationToken.None);

            Assert.Equal(31.68m, (await _db.FxRates.SingleAsync()).BuyRate);
        }

        [Fact]
        public async Task AddMissing_LeavesNothingPendingOnTheSharedContext()
        {
            // The context is request-scoped and shared with the rest of the portfolio work, so a
            // completed call must not leave tracked entities behind for someone else's SaveChanges.
            await _store.AddMissingAsync(
                [new(Day, "USD", 31.68m, 31.88m), new(Day, "USD", 31.68m, 31.88m)],
                CancellationToken.None);

            Assert.DoesNotContain(_db.ChangeTracker.Entries<FxRate>(), e => e.State == EntityState.Added);
        }

        [Fact]
        public async Task AddMissing_EmptyBatch_DoesNothing()
        {
            await _store.AddMissingAsync([], CancellationToken.None);
            Assert.Empty(await _db.FxRates.ToListAsync());
        }

        [Fact]
        public async Task AddMissing_OnlyCountsRowsFromItsOwnSource()
        {
            // 集保 and 國泰 publish different rates for the same day; one must not mask the other.
            _db.FxRates.Add(new FxRate
            {
                RateDate = Day, Currency = "USD", Source = FxRateSources.Tdcc,
                BuyRate = 31.60m, SellRate = 31.70m,
            });
            await _db.SaveChangesAsync();

            await _store.AddMissingAsync([new(Day, "USD", 31.68m, 31.88m)], CancellationToken.None);

            var stored = await _db.FxRates.OrderBy(r => r.Source).ToListAsync();
            Assert.Equal([FxRateSources.Cathay, FxRateSources.Tdcc], stored.Select(r => r.Source));
        }
    }
}
