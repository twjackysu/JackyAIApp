using System.Security.Claims;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.Assistant.Memory;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Tests.Services.Assistant.Memory
{
    /// <summary>
    /// What the reader asked Studio AI to remember (docs/ai-native/design/08): one line each, in their words, at most
    /// <see cref="AssistantMemoryService.Limit"/>, each reader's own.
    /// </summary>
    public class AssistantMemoryServiceTests : IDisposable
    {
        private const string User = "u1";

        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

        public void Dispose() => _db.Dispose();

        private AssistantMemoryService Service() => new(_db, _clock);

        [Fact]
        public async Task ANote_IsKeptOnOneLine_AndListedNewestFirst()
        {
            var first = await Service().AddAsync(User, "  在準備多益，\n目標   800 分  ", CancellationToken.None);
            _clock.Now = _clock.Now.AddMinutes(5);
            var second = await Service().AddAsync(User, "短文用商務情境", CancellationToken.None);

            Assert.Equal(MemoryAddOutcome.Added, first.Outcome);
            Assert.Equal("在準備多益， 目標 800 分", first.Memory!.Text);
            Assert.Equal(1, first.Count);
            Assert.Equal(2, second.Count);
            Assert.Equal(new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc), first.Memory.CreatedAt);

            var listed = await Service().ListAsync(User, CancellationToken.None);
            Assert.Equal(["短文用商務情境", "在準備多益， 目標 800 分"], listed.Select(m => m.Text));
            Assert.Equal(second.Memory!.Id, listed[0].Id);
        }

        [Fact]
        public async Task AnglesAreKeptFullWidth_AsThePromptShowsThem_SoTheNoteCanBeForgottenByThoseWords()
        {
            var kept = await Service().AddAsync(User, "目標 >800 分 <TOEIC>", CancellationToken.None);

            Assert.Equal("目標 ＞800 分 ＜TOEIC＞", kept.Memory!.Text);
            Assert.NotNull(await Service().RemoveTextAsync(User, "目標 ＞800 分 ＜TOEIC＞", CancellationToken.None));
            Assert.Empty(_db.AssistantMemories);
        }

        [Fact]
        public async Task TheSameWords_AreNotKeptTwice()
        {
            var kept = await Service().AddAsync(User, "I prefer British spelling", CancellationToken.None);
            var again = await Service().AddAsync(User, " i prefer british  spelling ", CancellationToken.None);

            Assert.Equal(MemoryAddOutcome.AlreadyKept, again.Outcome);
            Assert.Equal(kept.Memory!.Id, again.Memory!.Id);
            Assert.Equal(1, again.Count);
            Assert.Single(_db.AssistantMemories);
        }

        [Theory]
        [InlineData("", MemoryAddOutcome.Empty)]
        [InlineData(" \n\t ", MemoryAddOutcome.Empty)]
        public async Task NoWords_AreNotKept(string text, MemoryAddOutcome outcome)
        {
            var result = await Service().AddAsync(User, text, CancellationToken.None);

            Assert.Equal(outcome, result.Outcome);
            Assert.Null(result.Memory);
            Assert.Empty(_db.AssistantMemories);
        }

        [Fact]
        public async Task ANoteLongerThanTheColumn_IsRefused_ButOneThatFits_IsKept()
        {
            var fits = await Service().AddAsync(User, new string('字', AssistantMemoryService.MaxLength), CancellationToken.None);
            var tooLong = await Service().AddAsync(User, new string('長', AssistantMemoryService.MaxLength + 1), CancellationToken.None);

            Assert.Equal(MemoryAddOutcome.Added, fits.Outcome);
            Assert.Equal(MemoryAddOutcome.TooLong, tooLong.Outcome);
            Assert.Single(_db.AssistantMemories);
        }

        [Fact]
        public async Task AReaderKeepsAtMostTheLimit_AndOthersDoNotCount()
        {
            for (var i = 0; i < AssistantMemoryService.Limit; i++)
                Assert.Equal(MemoryAddOutcome.Added, (await Service().AddAsync(User, $"note {i}", CancellationToken.None)).Outcome);

            var full = await Service().AddAsync(User, "one more", CancellationToken.None);
            var someoneElse = await Service().AddAsync("u2", "one more", CancellationToken.None);

            Assert.Equal(MemoryAddOutcome.Full, full.Outcome);
            Assert.Equal(AssistantMemoryService.Limit, full.Count);
            Assert.Equal(MemoryAddOutcome.Added, someoneElse.Outcome);
            Assert.Equal(1, someoneElse.Count);
            // A note already kept is still answered as kept when the list is full.
            Assert.Equal(MemoryAddOutcome.AlreadyKept, (await Service().AddAsync(User, "note 3", CancellationToken.None)).Outcome);
        }

        [Fact]
        public async Task ForgettingANote_DeletesIt_OnlyForItsReader()
        {
            var mine = (await Service().AddAsync(User, "在準備多益", CancellationToken.None)).Memory!;
            await Service().AddAsync("u2", "別人的筆記", CancellationToken.None);

            Assert.Null(await Service().RemoveAsync("u2", mine.Id, CancellationToken.None));
            Assert.Null(await Service().RemoveAsync(User, "no-such-id", CancellationToken.None));
            Assert.Equal(2, _db.AssistantMemories.Count());

            var forgotten = await Service().RemoveAsync(User, mine.Id, CancellationToken.None);
            Assert.Equal(mine, forgotten);
            Assert.Empty(await Service().ListAsync(User, CancellationToken.None));
            Assert.Single(await Service().ListAsync("u2", CancellationToken.None));
        }

        private AssistantMemoryPage Page(string userId)
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test")),
            };
            return new AssistantMemoryPage(Service(), TestCurrentUser.Of(http));
        }

        [Fact]
        public async Task TheConnectorsPage_ListsTheReadersOwn_AndForgetsOnlyThose()
        {
            var mine = (await Service().AddAsync(User, "在準備多益", CancellationToken.None)).Memory!;
            var theirs = (await Service().AddAsync("u2", "別人的筆記", CancellationToken.None)).Memory!;

            var listed = (await Page(User).ListAsync(CancellationToken.None)).Data!;
            Assert.Equal(mine, Assert.Single(listed.Memories));
            Assert.Equal(AssistantMemoryService.Limit, listed.Limit);

            Assert.Equal(ErrorCodes.NotFound, (await Page(User).ForgetAsync(theirs.Id, CancellationToken.None)).Error);
            Assert.True((await Page(User).ForgetAsync(mine.Id, CancellationToken.None)).IsSuccess);
            Assert.Equal("u2", Assert.Single(_db.AssistantMemories).UserId);
        }

        [Fact]
        public async Task ANote_CanBeForgottenByItsWords_AsKept()
        {
            await Service().AddAsync(User, "在準備多益，目標 800 分", CancellationToken.None);
            await Service().AddAsync(User, "Short passages please", CancellationToken.None);
            await Service().AddAsync("u2", "在準備多益，目標 800 分", CancellationToken.None);

            Assert.Null(await Service().RemoveTextAsync(User, "在準備多益", CancellationToken.None));
            Assert.Null(await Service().RemoveTextAsync(User, "  ", CancellationToken.None));
            // Exactly the words kept (the card showed them): another case is another note.
            Assert.Null(await Service().RemoveTextAsync(User, "short passages please", CancellationToken.None));
            var forgotten = await Service().RemoveTextAsync(User, " Short passages\nplease ", CancellationToken.None);

            Assert.Equal("Short passages please", forgotten!.Text);
            Assert.Equal(["在準備多益，目標 800 分"], (await Service().ListAsync(User, CancellationToken.None)).Select(m => m.Text));
            Assert.Single(await Service().ListAsync("u2", CancellationToken.None));
        }
    }
}
