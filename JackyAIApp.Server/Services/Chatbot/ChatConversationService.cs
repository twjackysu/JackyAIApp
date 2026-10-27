using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
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
        /// The user's conversations with one assistant that have a message holding every word of <paramref name="query"/>
        /// (D-191), most recently updated first, at most <see cref="ChatConversationService.MaxSearchHits"/>, each with a
        /// snippet of its first such message around the first word. The history column's title search finds the rest on
        /// the client. An empty query finds nothing; one longer than <see cref="ChatConversationService.MaxSearchLength"/>
        /// is refused. Read only from what is stored: the visible text of each question and answer, not attachments.
        /// </summary>
        Task<ServiceResult<List<ChatSearchHitDto>>> SearchAsync(string profile, string? query, CancellationToken ct);

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

        /// <summary>The longest search the history column sends (its box holds the same).</summary>
        internal const int MaxSearchLength = 100;

        /// <summary>Words of a search past these are not looked for: each is one more condition on every message scanned.</summary>
        internal const int MaxSearchWords = 5;

        /// <summary>
        /// Only the reader's most recently updated conversations of the assistant are searched: each search reads every
        /// message of them (a LIKE on the text), and the database is small.
        /// </summary>
        internal const int SearchedConversations = 300;

        /// <summary>Conversations a search answers at most.</summary>
        internal const int MaxSearchHits = 20;

        /// <summary>Characters of a snippet before the word it was found by, and in all.</summary>
        internal const int SnippetBefore = 16;
        internal const int SnippetLength = 72;

        public async Task<ServiceResult<List<ChatSearchHitDto>>> SearchAsync(string profile, string? query, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (query != null && query.Length > MaxSearchLength)
                return ServiceResult.Fail(ErrorCodes.BadRequest, $"搜尋最多 {MaxSearchLength} 個字。");

            var words = SearchWords(query);
            if (words.Count == 0)
                return ServiceResult.Ok(new List<ChatSearchHitDto>());

            var recent = dbContext.ChatConversations
                .Where(c => c.UserId == userId && c.Profile == profile)
                .OrderByDescending(c => c.UpdatedAt)
                .Take(SearchedConversations)
                .Select(c => c.Id);
            // SQL Server's collation ignores case and width, as the client's title search does.
            var matching = dbContext.ChatMessages.Where(m => recent.Contains(m.ConversationId));
            foreach (var word in words)
                matching = matching.Where(m => m.Content.Contains(word));

            // One the client's title search already shows would only take a place another could have: left out. The
            // words padded to five with the first, so it is one condition whatever their number.
            var w = Enumerable.Range(0, MaxSearchWords).Select(i => i < words.Count ? words[i] : words[0]).ToArray();
            string w0 = w[0], w1 = w[1], w2 = w[2], w3 = w[3], w4 = w[4];
            var conversations = await matching
                .Where(m => m.Conversation!.Title == null || !(m.Conversation.Title.Contains(w0) && m.Conversation.Title.Contains(w1)
                    && m.Conversation.Title.Contains(w2) && m.Conversation.Title.Contains(w3) && m.Conversation.Title.Contains(w4)))
                .Select(m => new { m.ConversationId, m.Conversation!.UpdatedAt })
                .Distinct()
                .OrderByDescending(c => c.UpdatedAt)
                .Take(MaxSearchHits)
                .Select(c => c.ConversationId)
                .ToListAsync(ct);
            if (conversations.Count == 0)
                return ServiceResult.Ok(new List<ChatSearchHitDto>());

            // Each one's first matching message, read whole only for those.
            var firstIds = await matching
                .Where(m => conversations.Contains(m.ConversationId))
                .GroupBy(m => m.ConversationId)
                .Select(g => g.Min(m => m.Id))
                .ToListAsync(ct);
            var firsts = await dbContext.ChatMessages
                .Where(m => firstIds.Contains(m.Id))
                .Select(m => new { m.ConversationId, m.Id, m.Content })
                .ToDictionaryAsync(m => m.ConversationId, ct);

            var hits = conversations
                .Where(firsts.ContainsKey)
                .Select(id => new ChatSearchHitDto(id, firsts[id].Id, SnippetOf(firsts[id].Content, words[0])))
                .ToList();
            return ServiceResult.Ok(hits);
        }

        /// <summary>
        /// A search's words: split on white space (a control character counts as one), full-width forms made plain (NFKC),
        /// each once whatever its case, at most <see cref="MaxSearchWords"/>. A character outside the BMP (an emoji, 𠮷) is
        /// left out: the default collation gives it no weight, so a LIKE on it would match every message.
        /// </summary>
        internal static List<string> SearchWords(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return [];
            var bmp = new string(query.Where(c => !char.IsSurrogate(c)).ToArray());
            var spaced = new string(bmp.Normalize(NormalizationForm.FormKC)
                .Select(c => char.IsControl(c) ? ' ' : c).ToArray());
            return spaced
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxSearchWords)
                .ToList();
        }

        /// <summary>Markdown's marks around words in a stored answer (dropped) and between parts of it (a space).</summary>
        private static readonly char[] InlineMarks = ['*', '`'];
        private static readonly char[] BlockMarks = ['#', '|', '>'];

        /// <summary>
        /// A message on one line, without markdown's marks, cut to about <see cref="SnippetLength"/> characters around the
        /// first place <paramref name="word"/> is (any case or width; the start when it is not there), with … where it was cut.
        /// A cut never splits a surrogate pair.
        /// </summary>
        internal static string SnippetOf(string content, string word)
        {
            var plain = new string(content
                .Where(c => !InlineMarks.Contains(c))
                .Select(c => BlockMarks.Contains(c) || char.IsWhiteSpace(c) || char.IsControl(c) ? ' ' : c)
                .ToArray());
            var text = string.Join(' ', plain.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var plainWord = new string(word.Where(c => !InlineMarks.Contains(c) && !BlockMarks.Contains(c)).ToArray());
            var at = plainWord.Length == 0 ? -1 : CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                text, plainWord, CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth);
            var start = Math.Max(0, at - SnippetBefore);
            if (start > 0 && char.IsLowSurrogate(text[start])) start--;
            var end = Math.Min(text.Length, start + SnippetLength);
            if (end < text.Length && char.IsLowSurrogate(text[end])) end--;
            return (start > 0 ? "…" : "") + text[start..end].Trim() + (end < text.Length ? "…" : "");
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
