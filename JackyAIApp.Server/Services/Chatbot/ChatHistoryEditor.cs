using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>Where an attachment's file is kept, for deleting it once its row is gone.</summary>
    public sealed record StoredFile(string? BlobName, string? AnthropicFileId);

    /// <param name="KeptAttachments">The edited message's attachments, now unsent again, to go with
    /// its new wording.</param>
    /// <param name="RemovedFiles">The files of attachments removed with the later messages.</param>
    public sealed record RewindResult(List<SQL.ChatAttachment> KeptAttachments, List<StoredFile> RemovedFiles);

    /// <summary>
    /// Editing a message the reader sent: the conversation goes back to just before it — that message
    /// and everything after it are removed — and the new wording is then sent as a turn of its own,
    /// so the answer is to the edited message and nothing that followed the old one. (ChatGPT keeps
    /// the old branch to flip back to; this keeps only the latest.)
    /// </summary>
    public static class ChatHistoryEditor
    {
        /// <summary>
        /// Removes a user's message and every later one in its conversation, keeping its attachments
        /// (unsent again) for the edited message. Null when it is not one of the user's own messages
        /// in that conversation, or the conversation is another assistant's than <paramref name="profile"/>
        /// (the edit would be answered in a new conversation, so nothing here may go).
        /// </summary>
        public static async Task<RewindResult?> RewindAsync(
            AzureSQLDBContext db, string userId, string conversationId, long messageId, string profile, CancellationToken ct)
        {
            var target = await db.ChatMessages
                .Include(m => m.Attachments)
                .FirstOrDefaultAsync(m => m.Id == messageId && m.Role == "user" &&
                                          m.ConversationId == conversationId &&
                                          m.Conversation!.UserId == userId &&
                                          m.Conversation.Profile == profile, ct);
            if (target == null) return null;

            // Later messages by when they were written; the id breaks ties the clock cannot.
            var removed = await db.ChatMessages
                .Include(m => m.Attachments)
                .Where(m => m.ConversationId == conversationId &&
                            (m.CreatedAt > target.CreatedAt || (m.CreatedAt == target.CreatedAt && m.Id >= target.Id)))
                .ToListAsync(ct);

            var kept = target.Attachments.ToList();
            foreach (var attachment in kept)
            {
                attachment.MessageId = null;
                attachment.Message = null;
                target.Attachments.Remove(attachment);
            }

            var removedFiles = removed
                .SelectMany(m => m.Attachments)
                .Where(a => a.BlobName != null || a.AnthropicFileId != null)
                .Select(a => new StoredFile(a.BlobName, a.AnthropicFileId))
                .ToList();
            db.ChatMessages.RemoveRange(removed);
            await db.SaveChangesAsync(ct);

            return new RewindResult(kept, removedFiles);
        }
    }
}
