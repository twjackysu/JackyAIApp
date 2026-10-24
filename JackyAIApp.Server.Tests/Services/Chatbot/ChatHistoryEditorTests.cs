using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.Chatbot;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>Editing a message the reader sent: the conversation goes back to just before it.</summary>
    public class ChatHistoryEditorTests : IDisposable
    {
        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        private readonly DateTime _t0 = new(2026, 10, 21, 1, 0, 0, DateTimeKind.Utc);

        public void Dispose() => _db.Dispose();

        /// <summary>u1's conversation c1: four turns, the second question with two files.</summary>
        private async Task<SQL.ChatMessage[]> SeedAsync()
        {
            _db.ChatConversations.Add(new SQL.ChatConversation { Id = "c1", UserId = "u1" });
            var messages = new[]
            {
                Message(1, "user", "台積電？"),
                Message(2, "assistant", "收 1,050"),
                Message(3, "user", "看這份財報"),
                Message(4, "assistant", "毛利 59%"),
                Message(5, "user", "那鴻海？"),
                Message(6, "assistant", "收 210"),
            };
            messages[2].Attachments.Add(Attachment(10, "年報.pdf", "b10", "file_10"));
            messages[2].Attachments.Add(Attachment(11, "表.csv"));
            messages[4].Attachments.Add(Attachment(12, "圖.png", "b12", "file_12"));
            _db.ChatMessages.AddRange(messages);
            await _db.SaveChangesAsync();
            return messages;
        }

        private SQL.ChatMessage Message(long id, string role, string content) => new()
        {
            Id = id, ConversationId = "c1", Role = role, Content = content, CreatedAt = _t0.AddMinutes(id),
        };

        private static SQL.ChatAttachment Attachment(long id, string name, string? blob = null, string? fileId = null) => new()
        {
            Id = id, UserId = "u1", FileName = name, Kind = "text", Text = "", BlobName = blob, AnthropicFileId = fileId,
        };

        [Fact]
        public async Task TheEditedMessage_AndEverythingAfterIt_AreRemoved()
        {
            await SeedAsync();

            var result = await ChatHistoryEditor.RewindAsync(_db, "u1", "c1", 3, "finance", CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal([1L, 2L], await _db.ChatMessages.OrderBy(m => m.Id).Select(m => m.Id).ToListAsync());
        }

        [Fact]
        public async Task TheEditedMessagesFiles_AreKept_ToGoWithItsNewWording()
        {
            await SeedAsync();

            var result = await ChatHistoryEditor.RewindAsync(_db, "u1", "c1", 3, "finance", CancellationToken.None);

            Assert.Equal([10L, 11L], result!.KeptAttachments.Select(a => a.Id).OrderBy(id => id));
            // Unsent again, so the chat request can attach them to the edited message.
            Assert.All(await _db.ChatAttachments.Where(a => a.Id == 10 || a.Id == 11).ToListAsync(),
                a => Assert.Null(a.MessageId));
        }

        [Fact]
        public async Task LaterMessagesFiles_GoWithThem_AndAreReturnedForDeletingFromTheStores()
        {
            await SeedAsync();

            var result = await ChatHistoryEditor.RewindAsync(_db, "u1", "c1", 3, "finance", CancellationToken.None);

            Assert.Equal([new StoredFile("b12", "file_12")], result!.RemovedFiles);
            Assert.False(await _db.ChatAttachments.AnyAsync(a => a.Id == 12));
        }

        [Theory]
        [InlineData("u2", "c1", 3L)] // someone else's conversation
        [InlineData("u1", "c1", 4L)] // the assistant's message
        [InlineData("u1", "c2", 3L)] // the wrong conversation
        [InlineData("u1", "c1", 99L)] // no such message
        public async Task OnlyTheReadersOwnMessage_CanBeEdited(string userId, string conversationId, long messageId)
        {
            await SeedAsync();

            var result = await ChatHistoryEditor.RewindAsync(_db, userId, conversationId, messageId, "finance", CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(6, await _db.ChatMessages.CountAsync());
        }
    }
}
