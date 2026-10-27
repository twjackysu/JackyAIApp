using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Chatbot;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>
    /// A user's chat history, now behind <see cref="ChatConversationService"/>: each user reaches
    /// only their own conversations, and deleting one or rewinding for an edit takes its
    /// attachments' files with it. On SQLite, so the deletes cascade as they do on SQL Server.
    /// </summary>
    public class ChatConversationServiceTests : IDisposable
    {
        private readonly SqliteConnection _keepAlive;
        private readonly DbContextOptions<AzureSQLDBContext> _options;
        private readonly List<AzureSQLDBContext> _contexts = [];
        private readonly InMemoryChatAttachmentStore _store = new();
        private readonly Mock<IAnthropicFileStore> _anthropic = new();
        private readonly List<string> _anthropicDeleted = [];
        private readonly DateTime _t0 = new(2026, 10, 21, 1, 0, 0, DateTimeKind.Utc);

        public ChatConversationServiceTests()
        {
            var connectionString = $"Data Source=file:chat-conversations-{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keepAlive = new SqliteConnection(connectionString);
            _keepAlive.Open();
            _options = new DbContextOptionsBuilder<AzureSQLDBContext>().UseSqlite(connectionString).Options;
            using var db = new AzureSQLDBContext(_options);
            db.Database.EnsureCreated();

            _anthropic.Setup(a => a.DeleteAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Callback((IEnumerable<string> ids, CancellationToken _) => _anthropicDeleted.AddRange(ids))
                .Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            foreach (var db in _contexts) db.Dispose();
            _keepAlive.Dispose();
        }

        private AzureSQLDBContext Db()
        {
            var db = new AzureSQLDBContext(_options);
            _contexts.Add(db);
            return db;
        }

        private ChatConversationService As(string userId)
        {
            var db = Db();
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(userId);
            var attachments = new ChatAttachmentService(
                NullLogger<ChatAttachmentService>.Instance, db, _store, _anthropic.Object, user.Object);
            return new ChatConversationService(db, attachments, user.Object);
        }

        /// <summary>
        /// alice's c1: a question with a PDF and a CSV, its answer, a question with an image, its
        /// answer. bob's c2: one question with an image.
        /// </summary>
        private async Task SeedAsync()
        {
            var db = Db();
            db.ChatConversations.AddRange(
                new SQL.ChatConversation { Id = "c1", UserId = "alice", Title = "台積電", UpdatedAt = _t0 },
                new SQL.ChatConversation { Id = "c2", UserId = "bob", Title = "鴻海", UpdatedAt = _t0 });
            db.ChatMessages.AddRange(
                Message(1, "c1", "user", "看這份財報"),
                Message(2, "c1", "assistant", "毛利 59%"),
                Message(3, "c1", "user", "這張圖呢"),
                Message(4, "c1", "assistant", "是 K 線"),
                Message(5, "c2", "user", "鴻海？"));
            db.ChatAttachments.AddRange(
                Attachment(10, "alice", 1, "年報.pdf", "b10"),
                Attachment(11, "alice", 1, "表.csv"),
                Attachment(12, "alice", 3, "圖.png", "b12"),
                Attachment(13, "bob", 5, "圖.png", "b13"));
            await db.SaveChangesAsync();
            foreach (var blob in new[] { "b10", "b12", "b13" })
                await _store.SaveAsync(blob, [1], "image/png", default);
        }

        private SQL.ChatMessage Message(long id, string conversationId, string role, string content) => new()
        {
            Id = id, ConversationId = conversationId, Role = role, Content = content, CreatedAt = _t0.AddMinutes(id),
        };

        private static SQL.ChatAttachment Attachment(long id, string userId, long messageId, string name, string? blob = null) => new()
        {
            Id = id, UserId = userId, MessageId = messageId, FileName = name, Kind = "text", Text = "abc",
            BlobName = blob, AnthropicFileId = blob == null ? null : $"file_{blob}",
        };

        [Fact]
        public async Task List_HasOnlyYourOwn_MostRecentlyUpdatedFirst_UntitledAsANewConversation()
        {
            var db = Db();
            db.ChatConversations.AddRange(
                new SQL.ChatConversation { Id = "old", UserId = "alice", Title = "舊的", UpdatedAt = _t0 },
                new SQL.ChatConversation { Id = "new", UserId = "alice", Title = " ", UpdatedAt = _t0.AddHours(1) },
                new SQL.ChatConversation { Id = "bobs", UserId = "bob", Title = "bob", UpdatedAt = _t0.AddHours(2) });
            await db.SaveChangesAsync();

            var list = (await As("alice").ListAsync("finance", default)).Data!;

            Assert.Equal(["new", "old"], list.Select(c => c.Id));
            Assert.Equal(["新對話", "舊的"], list.Select(c => c.Title));
        }

        [Fact]
        public async Task Messages_AreOldestFirst_WithTheirAttachments()
        {
            await SeedAsync();

            var messages = (await As("alice").GetMessagesAsync("c1", default)).Data!;

            Assert.Equal([1L, 2L, 3L, 4L], messages.Select(m => m.Id));
            Assert.Equal(["年報.pdf", "表.csv"], messages[0].Attachments.Select(a => a.FileName));
            Assert.Equal(3, messages[0].Attachments[0].CharCount);
            Assert.Empty(messages[1].Attachments);
        }

        [Fact]
        public async Task SomeoneElsesConversation_IsNotFound_AndABlankIdIsABadRequest()
        {
            await SeedAsync();

            Assert.Equal(ErrorCodes.NotFound, (await As("alice").GetMessagesAsync("c2", default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await As("alice").DeleteAsync("c2", default)).Error);
            Assert.Equal(ErrorCodes.BadRequest, (await As("alice").GetMessagesAsync(" ", default)).Error);
            Assert.Equal(ErrorCodes.BadRequest, (await As("alice").DeleteAsync("", default)).Error);
            Assert.Equal(2, Db().ChatConversations.Count());
            Assert.Empty(_anthropicDeleted);
        }

        [Fact]
        public async Task Delete_TakesTheMessagesAttachmentsAndTheirFiles()
        {
            await SeedAsync();

            var result = await As("alice").DeleteAsync("c1", default);

            Assert.True(result.IsSuccess);
            var db = Db();
            Assert.Equal(["c2"], db.ChatConversations.Select(c => c.Id));
            Assert.Equal([5L], db.ChatMessages.Select(m => m.Id));
            Assert.Equal([13L], db.ChatAttachments.Select(a => a.Id));
            Assert.Null(await _store.ReadAsync("b10", default));
            Assert.Null(await _store.ReadAsync("b12", default));
            Assert.NotNull(await _store.ReadAsync("b13", default));
            Assert.Equal(["file_b10", "file_b12"], _anthropicDeleted.Order());
        }

        [Fact]
        public async Task RewindForEdit_KeepsTheEditedMessagesFiles_AndDeletesTheLaterOnes()
        {
            await SeedAsync();

            var kept = await As("alice").RewindForEditAsync("c1", 1, "finance", default);

            Assert.Equal([10L, 11L], kept!.Select(a => a.Id).Order());
            var db = Db();
            Assert.Equal([5L], db.ChatMessages.Select(m => m.Id));
            Assert.All(db.ChatAttachments.Where(a => a.Id == 10 || a.Id == 11), a => Assert.Null(a.MessageId));
            Assert.NotNull(await _store.ReadAsync("b10", default)); // kept, to go with the new wording
            Assert.Null(await _store.ReadAsync("b12", default));
            Assert.Equal(["file_b12"], _anthropicDeleted);
        }

        [Fact]
        public async Task RewindForEdit_OfSomeoneElsesMessage_IsNull_AndRemovesNothing()
        {
            await SeedAsync();

            Assert.Null(await As("alice").RewindForEditAsync("c2", 5, "finance", default));
            Assert.Null(await As("alice").RewindForEditAsync("c1", 2, "finance", default)); // an answer, not a question
            Assert.Equal(5, Db().ChatMessages.Count());
            Assert.Empty(_anthropicDeleted);
        }

        [Fact]
        public async Task RewindForEdit_InAnotherAssistantsConversation_IsNull_AndRemovesNothing()
        {
            // The edit would be answered in a new conversation of the asking assistant.
            await SeedAsync();

            Assert.Null(await As("alice").RewindForEditAsync("c1", 3, "data-analysis", default));
            Assert.Equal(5, Db().ChatMessages.Count());
        }

        [Fact]
        public async Task Messages_BringBackTheCardsAnAnswerShowed()
        {
            var db = Db();
            db.ChatConversations.Add(new SQL.ChatConversation { Id = "c1", UserId = "alice", UpdatedAt = _t0 });
            db.ChatMessages.AddRange(
                Message(1, "c1", "user", "每月營收"),
                new SQL.ChatMessage { Id = 2, ConversationId = "c1", Role = "assistant", Content = "如圖", CreatedAt = _t0.AddMinutes(2),
                    CardsJson = "[{\"kind\":\"chart\",\"title\":\"每月營收\"}]" });
            await db.SaveChangesAsync();

            var messages = (await As("alice").GetMessagesAsync("c1", default)).Data!;

            Assert.Null(messages[0].Cards);
            Assert.Equal("chart", messages[1].Cards!.Value[0].GetProperty("kind").GetString());
        }

        [Fact]
        public async Task List_HasOnlyTheNamedAssistantsConversations()
        {
            var db = Db();
            db.ChatConversations.AddRange(
                new SQL.ChatConversation { Id = "fin", UserId = "alice", Title = "台積電", UpdatedAt = _t0 },
                new SQL.ChatConversation { Id = "data", UserId = "alice", Title = "營收", UpdatedAt = _t0, Profile = "data-analysis" });
            await db.SaveChangesAsync();

            Assert.Equal(["fin"], (await As("alice").ListAsync("finance", default)).Data!.Select(c => c.Id));
            Assert.Equal(["data"], (await As("alice").ListAsync("data-analysis", default)).Data!.Select(c => c.Id));
        }

        [Fact]
        public async Task Rename_KeepsTheReadersTitleOnOneLine_AndLeavesWhenItWasLastChanged()
        {
            await SeedAsync();

            var result = await As("alice").RenameAsync("c1", "  台積電\n  財報\t重點 ", default);

            Assert.True(result.IsSuccess);
            Assert.Equal("台積電 財報 重點", result.Data!.Title);
            var stored = Db().ChatConversations.AsNoTracking().Single(c => c.Id == "c1");
            Assert.Equal("台積電 財報 重點", stored.Title);
            Assert.Equal(_t0, stored.UpdatedAt);
        }

        [Fact]
        public async Task Rename_RefusesAnEmptyOrTooLongTitle_AndSomeoneElsesConversation()
        {
            await SeedAsync();
            var alice = As("alice");

            Assert.Equal(ErrorCodes.BadRequest, (await alice.RenameAsync("c1", " \n ", default)).Error);
            Assert.Equal(ErrorCodes.BadRequest, (await alice.RenameAsync("c1", null, default)).Error);
            Assert.Equal(ErrorCodes.BadRequest, (await alice.RenameAsync("c1", new string('字', 41), default)).Error);
            Assert.Equal(ErrorCodes.BadRequest, (await alice.RenameAsync(" ", "新標題", default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await alice.RenameAsync("c2", "新標題", default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await alice.RenameAsync("nope", "新標題", default)).Error);

            var titles = Db().ChatConversations.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Title).ToList();
            Assert.Equal(["台積電", "鴻海"], titles);
        }

        [Fact]
        public async Task Rename_CountsAnEmojiAsOneCharacter()
        {
            await SeedAsync();
            var forty = string.Concat(Enumerable.Repeat("📈", 40));

            var result = await As("alice").RenameAsync("c1", forty, default);

            Assert.True(result.IsSuccess);
            Assert.Equal(forty, result.Data!.Title);
        }

        [Theory]
        [InlineData("台積電", "台積電")]
        [InlineData("  a\r\n\tb  ", "a b")]
        [InlineData("a\u3000b", "a b")]
        [InlineData("\n\t ", null)]
        public void CleanTitle_IsOneLine(string typed, string? expected) =>
            Assert.Equal(expected, ChatConversationService.CleanTitle(typed));
    
        /// <summary>
        /// alice's finance conversations: older "a" (台積電 in a question and an answer), newer "b" (台積電 毛利 in an
        /// answer), "c" with neither; a studio one and bob's, both saying 台積電.
        /// </summary>
        private async Task SeedSearchAsync()
        {
            var db = Db();
            db.ChatConversations.AddRange(
                new SQL.ChatConversation { Id = "a", UserId = "alice", Title = "第一個", UpdatedAt = _t0 },
                new SQL.ChatConversation { Id = "b", UserId = "alice", Title = "第二個", UpdatedAt = _t0.AddHours(1) },
                new SQL.ChatConversation { Id = "c", UserId = "alice", Title = "第三個", UpdatedAt = _t0.AddHours(2) },
                new SQL.ChatConversation { Id = "s", UserId = "alice", Title = "studio", Profile = "studio", UpdatedAt = _t0 },
                new SQL.ChatConversation { Id = "x", UserId = "bob", Title = "bob", UpdatedAt = _t0 });
            db.ChatMessages.AddRange(
                Message(1, "a", "user", "台積電今天怎麼了"),
                Message(2, "a", "assistant", "台積電收盤上漲"),
                Message(3, "b", "user", "幫我看財報"),
                Message(4, "b", "assistant", "**台積電**的毛利率是 59%"),
                Message(5, "c", "user", "鴻海呢"),
                Message(6, "s", "user", "台積電"),
                Message(7, "x", "user", "台積電"));
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task Search_FindsYourOwnConversationsOfThatAssistant_ByTheirMessages_MostRecentlyUpdatedFirst()
        {
            await SeedSearchAsync();

            var hits = (await As("alice").SearchAsync("finance", "台積電", default)).Data!;

            Assert.Equal(["b", "a"], hits.Select(h => h.ConversationId));
            Assert.Equal([4L, 1L], hits.Select(h => h.MessageId));
            // The first matching message, without markdown's marks.
            Assert.Equal("台積電的毛利率是 59%", hits[0].Snippet);
            Assert.Equal("台積電今天怎麼了", hits[1].Snippet);
        }

        [Fact]
        public async Task Search_NeedsEveryWordInOneMessage()
        {
            await SeedSearchAsync();

            var hits = (await As("alice").SearchAsync("finance", " 毛利  台積電 ", default)).Data!;

            Assert.Equal(["b"], hits.Select(h => h.ConversationId));
            Assert.Empty((await As("alice").SearchAsync("finance", "毛利 鴻海", default)).Data!);
        }

        [Fact]
        public async Task Search_LeavesOutTheConversationsWhoseTitleTheClientAlreadyFinds()
        {
            await SeedSearchAsync();
            var db = Db();
            db.ChatConversations.Single(c => c.Id == "a").Title = "台積電今天";
            await db.SaveChangesAsync();

            var hits = (await As("alice").SearchAsync("finance", "台積電", default)).Data!;

            Assert.Equal(["b"], hits.Select(h => h.ConversationId));
            // A title holding only some of the words is no find of the client's.
            Assert.Equal(["a"], (await As("alice").SearchAsync("finance", "台積電 上漲", default)).Data!.Select(h => h.ConversationId));
        }

        [Fact]
        public async Task Search_OfNothingFindsNothing_AndATooLongOneIsRefused()
        {
            await SeedSearchAsync();

            Assert.Empty((await As("alice").SearchAsync("finance", "  \n ", default)).Data!);
            Assert.Empty((await As("alice").SearchAsync("finance", null, default)).Data!);
            var tooLong = new string('字', ChatConversationService.MaxSearchLength + 1);
            Assert.Equal(ErrorCodes.BadRequest, (await As("alice").SearchAsync("finance", tooLong, default)).Error);
        }

        [Fact]
        public void SearchWords_AreSplitOnSpaceAndControlCharacters_FullWidthMadePlain_EachOnce_AtMostFive()
        {
            Assert.Equal(["ETF", "0050"], ChatConversationService.SearchWords("ＥＴＦ\t０050 etf"));
            Assert.Equal(ChatConversationService.MaxSearchWords, ChatConversationService.SearchWords("a b c d e f g").Count);
            // Outside the BMP the collation would match anything.
            Assert.Equal(["台積電"], ChatConversationService.SearchWords("😀 台積電📈"));
            Assert.Empty(ChatConversationService.SearchWords("😀 𠮷"));
        }

        [Fact]
        public void Snippet_FindsAWordWithAMarkTheSnippetDrops()
        {
            var snippet = ChatConversationService.SnippetOf(new string('字', 40) + " 用 `C#` 寫", "c#");

            Assert.StartsWith("…", snippet);
            Assert.Contains("用 C 寫", snippet);
        }

        [Fact]
        public void Snippet_IsOneLineAroundTheFirstFind_CutWithAnEllipsis()
        {
            var before = new string('前', 40);
            var after = new string('後', 100);
            var snippet = ChatConversationService.SnippetOf($"{before}\n\n## ＥＴＦ\n{after}", "etf");

            // SnippetBefore characters before the find: 15 of 前 and the space the heading's marks became.
            Assert.StartsWith("…" + new string('前', ChatConversationService.SnippetBefore - 1) + " ＥＴＦ 後", snippet);
            Assert.EndsWith("後…", snippet);
            Assert.Equal(ChatConversationService.SnippetLength + 2, snippet.Length);
        }

        [Fact]
        public void Snippet_NeverSplitsASurrogatePair()
        {
            var emoji = string.Concat(Enumerable.Repeat("😀", 60));
            // Both cuts would land between the halves of a pair: the start at 122 - 16, the end 72 later.
            var snippet = ChatConversationService.SnippetOf("x" + emoji + "y找我" + emoji, "找我");

            var body = snippet.Trim('…');
            Assert.True(char.IsHighSurrogate(body[0]));
            Assert.True(char.IsLowSurrogate(body[^1]));
            Assert.Contains("y找我", body);
        }
    }
}
