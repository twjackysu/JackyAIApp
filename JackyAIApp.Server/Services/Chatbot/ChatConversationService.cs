using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// The signed-in user's chat history: their conversations, a conversation's messages, deleting
    /// one, and going back to before a message they edit. Someone else's conversation answers
    /// NotFound. A turn itself is written by <see cref="IAnthropicChatService"/>.
    /// </summary>
    public interface IChatConversationService
    {
        /// <summary>The user's conversations with one assistant (a chat profile's key), most recently updated first.</summary>
        Task<ServiceResult<List<ChatConversationDto>>> ListAsync(string profile, CancellationToken ct);

        /// <summary>A conversation's messages, oldest first.</summary>
        Task<ServiceResult<List<ChatMessageDto>>> GetMessagesAsync(string conversationId, CancellationToken ct);

        /// <summary>
        /// Gives a conversation the title the reader typed (D-144): one line, its spaces run together, at most
        /// <see cref="ChatConversationService.MaxTitleLength"/> characters; an empty or longer one is refused,
        /// never cut. When it was last changed is left alone, so renaming does not move it up the list.
        /// </summary>
        Task<ServiceResult<ChatTitleResponse>> RenameAsync(string conversationId, string? title, CancellationToken ct);

        /// <summary>Deletes a conversation with its messages, attachments and their files.</summary>
        Task<ServiceResult<NoData>> DeleteAsync(string conversationId, CancellationToken ct);

        /// <summary>
        /// Removes one of the user's messages and every later one in its conversation
        /// (<see cref="ChatHistoryEditor"/>), deleting the removed attachments' files. Returns the
        /// edited message's attachments, unsent again, to go with its new wording; null when it is
        /// not one of the user's own messages in that conversation, or the conversation is not
        /// <paramref name="profile"/>'s.
        /// </summary>
        Task<List<ChatAttachment>?> RewindForEditAsync(string conversationId, long messageId, string profile, CancellationToken ct);
    }

    public class ChatConversationService(
        AzureSQLDBContext dbContext,
        IChatAttachmentService attachments,
        ICurrentUser currentUser) : IChatConversationService
    {
        private static ServiceFailure ConversationIdRequired => ServiceResult.Fail(ErrorCodes.BadRequest, "conversationId is required.");
        private static ServiceFailure ConversationNotFound => ServiceResult.Fail(ErrorCodes.NotFound, "Conversation not found.");

        public async Task<ServiceResult<List<ChatConversationDto>>> ListAsync(string profile, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var result = await dbContext.ChatConversations
                .Where(c => c.UserId == userId && c.Profile == profile)
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new ChatConversationDto
                {
                    Id = c.Id,
                    Title = string.IsNullOrWhiteSpace(c.Title) ? "新對話" : c.Title!,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                })
                .ToListAsync(ct);

            return ServiceResult.Ok(result);
        }

        public async Task<ServiceResult<List<ChatMessageDto>>> GetMessagesAsync(string conversationId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (string.IsNullOrWhiteSpace(conversationId))
                return ConversationIdRequired;

            var owned = await dbContext.ChatConversations
                .AnyAsync(c => c.Id == conversationId && c.UserId == userId, ct);
            if (!owned)
                return ConversationNotFound;

            var rows = await dbContext.ChatMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    m.CardsJson,
                    Message = new ChatMessageDto
                    {
                        Id = m.Id,
                        Role = m.Role,
                        Content = m.Content,
                        CreatedAt = m.CreatedAt,
                        Attachments = m.Attachments
                            .OrderBy(a => a.Id)
                            .Select(a => new ChatAttachmentDto
                            {
                                Id = a.Id,
                                FileName = a.FileName,
                                Kind = a.Kind,
                                CharCount = a.Text.Length,
                                Truncated = a.Truncated,
                                PageCount = a.PageCount,
                            })
                            .ToList(),
                    },
                })
                .ToListAsync(ct);

            // The cards go back as the JSON they were stored as (the stream's tool_result.card).
            var result = rows.Select(r =>
            {
                if (r.CardsJson != null)
                {
                    using var cards = JsonDocument.Parse(r.CardsJson);
                    r.Message.Cards = cards.RootElement.Clone();
                }
                return r.Message;
            }).ToList();

            return ServiceResult.Ok(result);
        }

        /// <summary>The longest title a reader may give a conversation (the client's box holds the same, D-144).</summary>
        internal const int MaxTitleLength = 40;

        public async Task<ServiceResult<ChatTitleResponse>> RenameAsync(string conversationId, string? title, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (string.IsNullOrWhiteSpace(conversationId))
                return ConversationIdRequired;

            var cleaned = CleanTitle(title);
            if (cleaned == null)
                return ServiceResult.Fail(ErrorCodes.BadRequest, "標題不能是空白。");
            if (new StringInfo(cleaned).LengthInTextElements > MaxTitleLength)
                return ServiceResult.Fail(ErrorCodes.BadRequest, $"標題最多 {MaxTitleLength} 個字。");

            var conversation = await dbContext.ChatConversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
            if (conversation == null)
                return ConversationNotFound;

            conversation.Title = cleaned;
            await dbContext.SaveChangesAsync(ct);
            return ServiceResult.Ok(new ChatTitleResponse(cleaned));
        }

        /// <summary>A typed title on one line: every run of white space or control characters one space, trimmed; null when nothing is left.</summary>
        internal static string? CleanTitle(string? title)
        {
            if (title == null) return null;
            var spaced = new string(title.Select(c => char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c).ToArray());
            var cleaned = string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return cleaned.Length == 0 ? null : cleaned;
        }

        public async Task<ServiceResult<NoData>> DeleteAsync(string conversationId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (string.IsNullOrWhiteSpace(conversationId))
                return ConversationIdRequired;

            var conversation = await dbContext.ChatConversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
            if (conversation == null)
                return ConversationNotFound;

            // The rows cascade (messages, then their attachments); the stored files do not.
            var files = await ChatAttachmentService.StoredFiles(dbContext.ChatAttachments
                .Where(a => a.Message!.ConversationId == conversationId)).ToListAsync(ct);

            dbContext.ChatConversations.Remove(conversation); // cascades to ChatMessages
            await dbContext.SaveChangesAsync(ct);
            await attachments.DeleteStoredFilesAsync(files, ct);

            return ServiceResult.Ok();
        }

        public async Task<List<ChatAttachment>?> RewindForEditAsync(string conversationId, long messageId, string profile, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var rewind = await ChatHistoryEditor.RewindAsync(dbContext, userId, conversationId, messageId, profile, ct);
            if (rewind == null)
                return null;

            await attachments.DeleteStoredFilesAsync(rewind.RemovedFiles, ct);
            return rewind.KeptAttachments;
        }
    }
}
