using System.Text;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Chatbot;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQL = JackyAIApp.Server.Data.Models.SQL;

using JackyAIApp.Server.Tests.TestHelpers;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>
    /// A user's chat attachments, now behind <see cref="ChatAttachmentService"/>: what an upload
    /// keeps, what it refuses, the unsent uploads it makes room by deleting, and that each user
    /// reaches only their own. On SQLite, since an upload deletes with ExecuteDeleteAsync.
    /// </summary>
    public class ChatAttachmentServiceTests : IDisposable
    {
        private readonly SqliteConnection _keepAlive;
        private readonly DbContextOptions<AzureSQLDBContext> _options;
        private readonly List<AzureSQLDBContext> _contexts = [];
        private readonly InMemoryChatAttachmentStore _store = new();
        private readonly Mock<IAnthropicFileStore> _anthropic = new();
        private readonly List<string> _anthropicDeleted = [];

        public ChatAttachmentServiceTests()
        {
            var connectionString = $"Data Source=file:chat-attachments-{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keepAlive = new SqliteConnection(connectionString);
            _keepAlive.Open();
            _options = new DbContextOptionsBuilder<AzureSQLDBContext>().UseSqlite(connectionString).Options;
            using var db = new AzureSQLDBContext(_options);
            db.Database.EnsureCreated();

            _anthropic.Setup(a => a.UploadAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("file_new");
            _anthropic.Setup(a => a.DeleteAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Callback((IEnumerable<string> ids, CancellationToken _) => _anthropicDeleted.AddRange(ids))
                .Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            foreach (var db in _contexts) db.Dispose();
            _keepAlive.Dispose();
        }

        private ChatAttachmentService As(string userId)
        {
            var db = new AzureSQLDBContext(_options);
            _contexts.Add(db);
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(userId);
            return new ChatAttachmentService(NullLogger<ChatAttachmentService>.Instance, db, _store, _anthropic.Object, user.Object);
        }

        private AzureSQLDBContext Db()
        {
            var db = new AzureSQLDBContext(_options);
            _contexts.Add(db);
            return db;
        }

        private static SQL.ChatAttachment Attachment(long id, string userId, string? blob = null, long? messageId = null, DateTime? createdAt = null) => new()
        {
            Id = id, UserId = userId, FileName = $"{id}.txt", Kind = "text", Text = "內容", BlobName = blob,
            AnthropicFileId = blob == null ? null : $"file_{blob}", MediaType = blob == null ? null : "image/png",
            MessageId = messageId, CreatedAt = createdAt ?? DateTime.UtcNow,
        };

        /// <summary>Seeds attachments, storing a file for each one that has a blob.</summary>
        private async Task SeedAsync(params SQL.ChatAttachment[] attachments)
        {
            var db = Db();
            foreach (var a in attachments.Where(a => a.BlobName != null))
                await _store.SaveAsync(a.BlobName!, [1, 2, 3], "image/png", default);
            db.ChatAttachments.AddRange(attachments);
            await db.SaveChangesAsync();
        }

        /// <summary>u1's conversation c1 with one user message (id 100).</summary>
        private async Task SeedMessageAsync(string userId = "alice")
        {
            var db = Db();
            db.ChatConversations.Add(new SQL.ChatConversation { Id = "c1", UserId = userId });
            db.ChatMessages.Add(new SQL.ChatMessage { Id = 100, ConversationId = "c1", Role = "user", Content = "看這份" });
            await db.SaveChangesAsync();
        }

        private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text));

        private static MemoryStream Png() => new(TestImages.Png(20, 20));

        [Fact]
        public async Task ATextFile_IsKeptAsItsText_WithNoStoredFile()
        {
            using var content = Text("台積電收 1,050");

            var result = await As("alice").UploadAsync("folder/筆記.txt", content.Length, content, default);

            Assert.True(result.IsSuccess);
            Assert.Equal("筆記.txt", result.Data!.FileName);
            Assert.Equal("text", result.Data.Kind);
            Assert.Equal("台積電收 1,050".Length, result.Data.CharCount);
            var row = Assert.Single(Db().ChatAttachments);
            Assert.Equal(("alice", "台積電收 1,050", (string?)null, (long?)null), (row.UserId, row.Text, row.BlobName, row.MessageId));
            _anthropic.Verify(a => a.UploadAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AnImage_IsStored_AndUploadedToAnthropicOnce()
        {
            using var content = Png();

            var result = await As("alice").UploadAsync("圖.png", content.Length, content, default);

            Assert.Equal("image", result.Data!.Kind);
            var row = Assert.Single(Db().ChatAttachments);
            Assert.NotNull(row.BlobName);
            Assert.NotNull(row.MediaType);
            Assert.Equal("file_new", row.AnthropicFileId);
            var stored = await _store.ReadAsync(row.BlobName!, default);
            Assert.NotNull(stored);
            _anthropic.Verify(a => a.UploadAsync(stored, row.MediaType!, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AnUnsupportedTooLargeOrEmptyFile_IsRefused_AndNothingIsKept()
        {
            using var exe = Text("MZ");
            using var big = Text("x");
            using var blank = Text("   ");

            var unsupported = await As("alice").UploadAsync("setup.exe", exe.Length, exe, default);
            var tooLarge = await As("alice").UploadAsync("big.txt", ChatAttachmentLimits.MaxUploadBytes + 1, big, default);
            var empty = await As("alice").UploadAsync("blank.txt", blank.Length, blank, default);

            Assert.Equal(ErrorCodes.AttachmentUnsupportedType, unsupported.Error);
            Assert.Equal(ErrorCodes.PayloadTooLarge, tooLarge.Error);
            Assert.Equal(ErrorCodes.AttachmentUnreadable, empty.Error);
            Assert.Equal("The file has no text.", empty.ErrorMessage);
            Assert.Empty(Db().ChatAttachments);
        }

        [Fact]
        public async Task AnImageLargerThanTheBrowserMakesOne_AsksForAReload_AndKeepsNothing()
        {
            // A page loaded before images were downsized in the browser sends the photo as it is.
            using var content = new MemoryStream(TestImages.Jpeg(4032, 3024));

            var result = await As("alice").UploadAsync("相片.jpg", content.Length, content, default);

            Assert.Equal(ErrorCodes.AttachmentImageTooLarge, result.Error);
            Assert.Empty(Db().ChatAttachments);
            _anthropic.Verify(a => a.UploadAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AnUpload_MakesRoom_KeepingTheNewestFewUnsent_AndDeletesTheRestWithTheirFiles()
        {
            await SeedMessageAsync();
            await SeedAsync(
                Attachment(1, "alice", blob: "b1"),
                Attachment(2, "alice", blob: "b2"),
                Attachment(3, "alice"),
                Attachment(4, "alice"),
                Attachment(5, "alice"),
                // The newest id, but past a day old.
                Attachment(6, "alice", blob: "b6", createdAt: DateTime.UtcNow.AddDays(-2)),
                // Sent, and someone else's: neither is touched.
                Attachment(7, "alice", blob: "b7", messageId: 100),
                Attachment(8, "bob", blob: "b8"));
            using var content = Text("新的");

            var result = await As("alice").UploadAsync("新.txt", content.Length, content, default);

            var left = Db().ChatAttachments.OrderBy(a => a.Id).Select(a => a.Id).ToList();
            Assert.Equal([2L, 3L, 4L, 5L, 7L, 8L, result.Data!.Id], left);
            Assert.Null(await _store.ReadAsync("b1", default));
            Assert.Null(await _store.ReadAsync("b6", default));
            Assert.NotNull(await _store.ReadAsync("b2", default));
            Assert.Equal(["file_b1", "file_b6"], _anthropicDeleted.Order());
        }

        [Fact]
        public async Task DeleteUnsent_RemovesYourOwnUpload_AndItsFiles()
        {
            await SeedAsync(Attachment(1, "alice", blob: "b1"));

            var result = await As("alice").DeleteUnsentAsync(1, default);

            Assert.True(result.IsSuccess);
            Assert.Empty(Db().ChatAttachments);
            Assert.Null(await _store.ReadAsync("b1", default));
            Assert.Equal(["file_b1"], _anthropicDeleted);
        }

        [Fact]
        public async Task ASentOrSomeoneElsesUpload_IsNotFound_ToDelete()
        {
            await SeedMessageAsync();
            await SeedAsync(Attachment(1, "alice", blob: "b1", messageId: 100), Attachment(2, "bob", blob: "b2"));

            Assert.Equal(ErrorCodes.NotFound, (await As("alice").DeleteUnsentAsync(1, default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await As("alice").DeleteUnsentAsync(2, default)).Error);
            Assert.Equal(2, Db().ChatAttachments.Count());
            Assert.Empty(_anthropicDeleted);
        }

        [Fact]
        public async Task Content_IsYourOwnStoredFile_SentOrNot()
        {
            await SeedMessageAsync();
            await SeedAsync(
                Attachment(1, "alice", blob: "b1", messageId: 100),
                Attachment(2, "bob", blob: "b2"),
                Attachment(3, "alice"),
                Attachment(4, "alice", blob: "gone"));
            await _store.DeleteAsync(["gone"], default);

            var own = await As("alice").GetContentAsync(1, default);

            Assert.Equal(new byte[] { 1, 2, 3 }, own.Data!.Bytes);
            Assert.Equal("image/png", own.Data.MediaType);
            Assert.Equal(ErrorCodes.NotFound, (await As("alice").GetContentAsync(2, default)).Error); // bob's
            Assert.Equal(ErrorCodes.NotFound, (await As("alice").GetContentAsync(3, default)).Error); // text, no file
            Assert.Equal(ErrorCodes.NotFound, (await As("alice").GetContentAsync(4, default)).Error); // file gone
        }

        [Fact]
        public async Task TakeUnsent_LeavesOutOthersAndSent_AtMostAMessagesWorth_InUploadOrder()
        {
            await SeedMessageAsync();
            await SeedAsync(
                Attachment(1, "alice"), Attachment(2, "alice"), Attachment(3, "alice"), Attachment(4, "alice"),
                Attachment(5, "alice"), Attachment(6, "alice"), Attachment(7, "alice"),
                Attachment(8, "bob"),
                Attachment(9, "alice", messageId: 100));

            // Distinct, then the first five asked for (7, 1, 8, 9, 3), of which alice's unsent ones.
            var taken = await As("alice").TakeUnsentAsync([7, 1, 1, 8, 9, 3, 2, 4, 5, 6], default);

            Assert.Equal([1L, 3L, 7L], taken.Select(a => a.Id));
            Assert.Empty(await As("alice").TakeUnsentAsync(null, default));
        }

        [Fact]
        public async Task SentWith_IsYourAttachmentsOfThatMessage()
        {
            await SeedMessageAsync();
            await SeedAsync(Attachment(1, "alice", messageId: 100), Attachment(2, "alice"));

            Assert.Equal([1L], (await As("alice").SentWithAsync(100, default)).Select(a => a.Id));
            Assert.Empty(await As("bob").SentWithAsync(100, default));
        }
    }
}
