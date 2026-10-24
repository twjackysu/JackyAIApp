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
    }
}
