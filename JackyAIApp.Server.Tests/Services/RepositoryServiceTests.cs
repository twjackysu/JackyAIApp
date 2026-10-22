using Microsoft.Extensions.Caching.Memory;
using System.Data.Common;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Dictionary;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Services.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// A word can be in a user's collection once. Adding checks first, but two adds landing
    /// together (a double click) both passed the check, and the duplicate row they left made
    /// deleting that word throw ("Sequence contains more than one element") — so it could never be
    /// removed. These run on SQLite, which enforces the unique (UserId, WordId) index the way SQL
    /// Server does; the InMemory provider ignores indexes and could not show the race at all.
    /// </summary>
    public class RepositoryServiceTests : IDisposable
    {
        private const string UserId = "repository-test-user";

        private readonly SqliteConnection _keepAliveConnection;
        private readonly DbContextOptions<AzureSQLDBContext> _options;
        private readonly Mock<IDictionaryService> _dictionary = new();

        public RepositoryServiceTests()
        {
            var connectionString = $"Data Source=file:repository-{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keepAliveConnection = new SqliteConnection(connectionString);
            _keepAliveConnection.Open();
            _options = new DbContextOptionsBuilder<AzureSQLDBContext>().UseSqlite(connectionString).Options;

            using var db = new AzureSQLDBContext(_options);
            db.Database.EnsureCreated();
            db.Users.Add(new User { Id = UserId, LastUpdated = DateTime.UtcNow });
            db.SaveChanges();
        }

        public void Dispose() => _keepAliveConnection.Dispose();

        private RepositoryService CreateService(AzureSQLDBContext db)
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(UserId);
            return new RepositoryService(
                NullLogger<RepositoryService>.Instance, db, user.Object, _dictionary.Object,
                new MemoryCache(new MemoryCacheOptions()),
                new UserAccountService(NullLogger<UserAccountService>.Instance, db, user.Object, Mock.Of<IDrawdownBrakeAccess>()));
        }

        private async Task SeedWordsAsync(params string[] ids)
        {
            await using var db = new AzureSQLDBContext(_options);
            foreach (var id in ids)
            {
                db.Words.Add(new Word
                {
                    Id = id, WordText = id, KKPhonics = "", DateAdded = DateTime.UtcNow, LastUpdated = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        private async Task<List<string>> CollectionAsync()
        {
            await using var db = new AzureSQLDBContext(_options);
            return await db.UserWords.Where(uw => uw.UserId == UserId).Select(uw => uw.WordId).ToListAsync();
        }

        /// <summary>A context that, just before its own save, lets another request add the same
        /// rows — reproducing two adds that both passed the "already added?" check.</summary>
        private AzureSQLDBContext ContextWithRacingAdd(params string[] racingWordIds)
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>(_options)
                .AddInterceptors(new RacingAddInterceptor(_options, racingWordIds))
                .Options;
            return new AzureSQLDBContext(options);
        }

        [Fact]
        public async Task AddWordAsync_SameWordTwice_SecondIsRejectedAsAlreadyAdded()
        {
            await SeedWordsAsync("advocate");

            await using (var db = new AzureSQLDBContext(_options))
                Assert.True((await CreateService(db).AddWordAsync("advocate")).IsSuccess);
            await using (var db = new AzureSQLDBContext(_options))
            {
                var second = await CreateService(db).AddWordAsync("advocate");
                Assert.Equal(ErrorCodes.BadRequest, second.Error);
            }

            Assert.Equal(new[] { "advocate" }, await CollectionAsync());
        }

        [Fact]
        public async Task AddWordAsync_DoubleClickRace_LeavesOneRowAndDoesNotThrow()
        {
            await SeedWordsAsync("advocate");
            await using var db = ContextWithRacingAdd("advocate");

            var result = await CreateService(db).AddWordAsync("advocate");

            // The racing request won; this one reports the word as already there instead of a 500.
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
            Assert.Equal(new[] { "advocate" }, await CollectionAsync());
        }

        [Fact]
        public void UserWords_RejectsTheSameWordTwiceForOneUser()
        {
            using var db = new AzureSQLDBContext(_options);
            db.Words.Add(new Word { Id = "w", WordText = "w", KKPhonics = "", DateAdded = DateTime.UtcNow, LastUpdated = DateTime.UtcNow });
            db.UserWords.Add(new UserWord { UserId = UserId, WordId = "w", DateAdded = DateTime.UtcNow });
            db.UserWords.Add(new UserWord { UserId = UserId, WordId = "w", DateAdded = DateTime.UtcNow });

            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        [Fact]
        public async Task AddWordPackAsync_PackClickedTwiceRace_AddsTheRestWithoutDuplicates()
        {
            var pack = WordPacks.All[0];
            await SeedWordsAsync(pack.Words.ToArray());
            foreach (var text in pack.Words)
            {
                _dictionary.Setup(d => d.GetWordAsync(text)).ReturnsAsync(ServiceResult.Ok(new DTO.Word
                {
                    Id = text, PartitionKey = text, DateAdded = DateTime.UtcNow, SeoTitle = "", SeoDescription = "",
                    Word = text, KKPhonics = "", Meanings = [],
                }));
            }
            // The other request got the first two words in before this one saved.
            await using var db = ContextWithRacingAdd(pack.Words[0], pack.Words[1]);

            var result = await CreateService(db).AddWordPackAsync(pack.Id);

            Assert.True(result.IsSuccess);
            var counts = Assert.IsType<DTO.AddWordPackResultDto>(result.Data);
            Assert.Equal(pack.Words.Count - 2, counts.Added);
            Assert.Equal(2, counts.AlreadyHad);
            Assert.Equal(pack.Words.OrderBy(w => w), (await CollectionAsync()).OrderBy(w => w));
        }

        [Fact]
        public async Task DeleteWordAsync_RemovesEveryCopyOfTheWord()
        {
            // Duplicates can no longer be written, but rows left from before the unique index were
            // what made delete throw; InMemory doesn't enforce the index, so it can hold them.
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            await using var db = new AzureSQLDBContext(options);
            db.Users.Add(new User { Id = UserId, LastUpdated = DateTime.UtcNow });
            db.UserWords.Add(new UserWord { UserId = UserId, WordId = "advocate", DateAdded = DateTime.UtcNow });
            db.UserWords.Add(new UserWord { UserId = UserId, WordId = "advocate", DateAdded = DateTime.UtcNow });
            db.UserWords.Add(new UserWord { UserId = UserId, WordId = "other", DateAdded = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var result = await CreateService(db).DeleteWordAsync("advocate");

            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { "other" }, await db.UserWords.Select(uw => uw.WordId).ToListAsync());
        }

        [Fact]
        public async Task DeleteWordAsync_WordNotInCollection_IsNotFound()
        {
            await using var db = new AzureSQLDBContext(_options);
            var result = await CreateService(db).DeleteWordAsync("missing");
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        /// <summary>Inserts the given words for the test user through a separate context the first
        /// time the intercepted context saves — after its service has checked, before it writes.</summary>
        private sealed class RacingAddInterceptor(
            DbContextOptions<AzureSQLDBContext> options, string[] wordIds) : SaveChangesInterceptor
        {
            private bool _done;

            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (!_done)
                {
                    _done = true;
                    await using var other = new AzureSQLDBContext(options);
                    foreach (var id in wordIds)
                        other.UserWords.Add(new UserWord { UserId = UserId, WordId = id, DateAdded = DateTime.UtcNow });
                    await other.SaveChangesAsync(cancellationToken);
                }
                return result;
            }
        }
    }
}
