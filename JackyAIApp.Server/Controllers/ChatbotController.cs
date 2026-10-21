using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Chatbot;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController(
        ILogger<ChatbotController> logger,
        IMyResponseFactory responseFactory,
        IUserService userService,
        IAnthropicChatService chatService,
        IUsageLimitService usageLimitService,
        UsageLedger usageLedger,
        IChatAttachmentStore attachmentStore,
        IAnthropicFileStore anthropicFiles,
        ChatTurnRegistry turns,
        AzureSQLDBContext dbContext) : ControllerBase
    {
        private readonly ChatTurnRegistry _turns = turns;
        private readonly IChatAttachmentStore _attachmentStore = attachmentStore;
        private readonly IAnthropicFileStore _anthropicFiles = anthropicFiles;
        private readonly ILogger<ChatbotController> _logger = logger;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IUserService _userService = userService;
        private readonly IAnthropicChatService _chatService = chatService;
        private readonly IUsageLimitService _usageLimitService = usageLimitService;
        private readonly UsageLedger _usageLedger = usageLedger;
        private readonly AzureSQLDBContext _dbContext = dbContext;

        /// <summary>
        /// Streams a chat turn from Claude (with the TW-stock MCP server attached) back to the
        /// client as SSE, persisting the exchange to the caller's local conversation history.
        /// </summary>
        [HttpPost("chat")]
        public async Task Chat([FromBody] ChatRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Message))
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("Message cannot be null or empty.");
                return;
            }

            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                Response.StatusCode = 401;
                await Response.WriteAsync("Not authenticated.");
                return;
            }
            _logger.LogInformation("User {UserId} started chat", userId);

            // Only the caller's own uploads that have not been sent yet; an id that is someone
            // else's, already sent, or deleted is dropped rather than failing the message.
            var attachmentIds = (request.AttachmentIds ?? []).Distinct().Take(MaxAttachmentsPerMessage).ToList();
            var attachments = attachmentIds.Count == 0
                ? []
                : await _dbContext.ChatAttachments
                    .Where(a => attachmentIds.Contains(a.Id) && a.UserId == userId && a.MessageId == null)
                    .OrderBy(a => a.Id)
                    .ToListAsync(HttpContext.RequestAborted);

            // An edit sends the edited message's attachments again with its new wording.
            var editId = request.EditMessageId;
            if (editId != null && string.IsNullOrEmpty(request.ConversationId))
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("An edit needs its conversation.");
                return;
            }
            var editAttachments = editId == null
                ? []
                : await _dbContext.ChatAttachments
                    .Where(a => a.MessageId == editId && a.UserId == userId)
                    .ToListAsync(HttpContext.RequestAborted);

            // Charge per message up front (anti-abuse / cost control), plus each attachment. Charged
            // here rather than through [UsageLimit] because the cost depends on the attachments.
            // Whether it is refunded is UsageLedger's rule, like every other charge: the stream has
            // sent 200 before it can fail, so a failure is reported to the ledger below.
            var cost = UsageCosts.ChatMessage + attachments.Concat(editAttachments)
                .Aggregate(0UL, (sum, a) => sum + UsageCosts.ForChatAttachment(a.PageCount));
            if (!await _usageLimitService.TryConsumeAsync(userId, cost, "chatbot_message"))
            {
                Response.StatusCode = 429; // Too Many Requests
                await Response.WriteAsync("Daily usage limit exceeded.");
                return;
            }

            // Only once charged: a refused message must not have cut the conversation short.
            if (editId != null)
            {
                var rewind = await ChatHistoryEditor.RewindAsync(
                    _dbContext, userId, request.ConversationId!, editId.Value, HttpContext.RequestAborted);
                if (rewind == null)
                {
                    // Refunded at the end of the request: a 404 with no AI call.
                    Response.StatusCode = 404;
                    await Response.WriteAsync("Message not found.");
                    return;
                }
                attachments = [.. rewind.KeptAttachments.Concat(attachments).Take(MaxAttachmentsPerMessage)];
                await DeleteStoredFilesAsync(rewind.RemovedFiles, HttpContext.RequestAborted);
            }

            try
            {
                var result = await _chatService.StreamChatAsync(
                    userId, request.Message, request.ConversationId, attachments, Response, HttpContext.RequestAborted);

                // The service has already told the client why (an SSE error event with a code).
                if (!result.StreamingStarted)
                    _usageLedger.MarkFailed();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during chat streaming");
                _usageLedger.MarkFailed();
                if (!Response.HasStarted)
                {
                    Response.StatusCode = 500;
                    await Response.WriteAsync("Internal server error");
                }
            }
        }

        /// <summary>Files sent with one message, and unsent uploads kept per user.</summary>
        internal const int MaxAttachmentsPerMessage = 5;

        /// <summary>An upload never sent is deleted once it is this old.</summary>
        internal static readonly TimeSpan UnsentAttachmentLifetime = TimeSpan.FromHours(24);

        /// <summary>
        /// Readies a file to be sent with the next chat message: a Word, Excel, CSV or text file is
        /// read into text and only the text is kept; an image (scaled down to what the model reads)
        /// or a PDF is kept as a file. Free in itself: the charge is on the message that sends it.
        /// </summary>
        [HttpPost("attachments")]
        [RequestSizeLimit(MaxUploadBytes + 64 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes + 64 * 1024)]
        public async Task<IActionResult> UploadAttachment(IFormFile? file, CancellationToken ct = default)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");
            if (file == null || file.Length == 0)
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "A file is required.");

            var fileName = Path.GetFileName(file.FileName);
            var kind = AttachmentTextExtractor.KindOf(fileName);
            if (kind == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.AttachmentUnsupportedType, "Unsupported file type.");
            if (file.Length > MaxUploadBytes)
                return _responseFactory.CreateErrorResponse(ErrorCodes.PayloadTooLarge, "File is too large.");

            var attachment = new ChatAttachment
            {
                UserId = userId,
                FileName = fileName.Length > 260 ? fileName[^260..] : fileName,
                Kind = kind,
                Text = "",
            };
            PreparedAttachmentFile? prepared = null;
            try
            {
                await using var stream = file.OpenReadStream();
                if (kind == AttachmentKinds.Image)
                    prepared = AttachmentFileProcessor.PrepareImage(stream);
                else if (kind == AttachmentKinds.Pdf)
                    prepared = AttachmentFileProcessor.PreparePdf(stream);
                else
                {
                    var extracted = AttachmentTextExtractor.Extract(kind, stream);
                    if (string.IsNullOrWhiteSpace(extracted.Text))
                        return _responseFactory.CreateErrorResponse(ErrorCodes.AttachmentUnreadable, "The file has no text.");
                    attachment.Text = extracted.Text;
                    attachment.Truncated = extracted.Truncated;
                }
            }
            catch (UnreadableAttachmentException ex)
            {
                _logger.LogWarning(ex, "User {UserId} uploaded an unreadable {Kind} attachment", userId, kind);
                return _responseFactory.CreateErrorResponse(ErrorCodes.AttachmentUnreadable, "The file could not be read.");
            }
            catch (TooManyPagesException ex)
            {
                _logger.LogInformation("User {UserId} uploaded a PDF of {Pages} pages", userId, ex.Pages);
                return _responseFactory.CreateErrorResponse(ErrorCodes.AttachmentTooManyPages, "The PDF has too many pages.");
            }

            await DeleteUnsentAttachmentsAsync(userId, ct);

            if (prepared != null)
            {
                attachment.BlobName = Guid.NewGuid().ToString("N");
                attachment.MediaType = prepared.MediaType;
                attachment.PageCount = prepared.PageCount;
                await _attachmentStore.SaveAsync(attachment.BlobName, prepared.Bytes, prepared.MediaType, ct);
                // Uploaded once here, then only referred to on each turn. If this fails, the first
                // message that sends the file tries again (AnthropicChatService).
                attachment.AnthropicFileId = await _anthropicFiles.UploadAsync(prepared.Bytes, prepared.MediaType, ct);
            }
            _dbContext.ChatAttachments.Add(attachment);
            await _dbContext.SaveChangesAsync(ct);

            return _responseFactory.CreateOKResponse(ToDto(attachment));
        }

        /// <summary>Largest upload of any kind.</summary>
        private const long MaxUploadBytes = AttachmentTextExtractor.MaxBytes > AttachmentFileProcessor.MaxBytes
            ? AttachmentTextExtractor.MaxBytes
            : AttachmentFileProcessor.MaxBytes;

        /// <summary>
        /// Uploads that were never sent (a message that failed, a tab that was closed) are not kept:
        /// anything past a day old, and past the newest few, makes room for the one being uploaded.
        /// The client never holds more than one message's worth of unsent files.
        /// </summary>
        private async Task DeleteUnsentAttachmentsAsync(string userId, CancellationToken ct)
        {
            var staleBefore = DateTime.UtcNow - UnsentAttachmentLifetime;
            var keep = await _dbContext.ChatAttachments
                .Where(a => a.UserId == userId && a.MessageId == null && a.CreatedAt >= staleBefore)
                .OrderByDescending(a => a.Id)
                .Select(a => a.Id)
                .Take(MaxAttachmentsPerMessage - 1)
                .ToListAsync(ct);
            var stale = _dbContext.ChatAttachments
                .Where(a => a.UserId == userId && a.MessageId == null && !keep.Contains(a.Id));
            var files = await StoredFiles(stale).ToListAsync(ct);
            await stale.ExecuteDeleteAsync(ct);
            await DeleteStoredFilesAsync(files, ct);
        }

        /// <summary>Where the files of these attachments are kept, for those that have any.</summary>
        private static IQueryable<StoredFile> StoredFiles(IQueryable<ChatAttachment> attachments) =>
            attachments
                .Where(a => a.BlobName != null || a.AnthropicFileId != null)
                .Select(a => new StoredFile(a.BlobName, a.AnthropicFileId));

        /// <summary>Deletes attachments' files from both stores, once their rows are gone.</summary>
        private async Task DeleteStoredFilesAsync(IReadOnlyCollection<StoredFile> files, CancellationToken ct)
        {
            var blobs = files.Where(f => f.BlobName != null).Select(f => f.BlobName!).ToList();
            var fileIds = files.Where(f => f.AnthropicFileId != null).Select(f => f.AnthropicFileId!).ToList();
            if (blobs.Count > 0) await _attachmentStore.DeleteAsync(blobs, ct);
            if (fileIds.Count > 0) await _anthropicFiles.DeleteAsync(fileIds, ct);
        }

        private static ChatAttachmentDto ToDto(ChatAttachment a) => new()
        {
            Id = a.Id,
            FileName = a.FileName,
            Kind = a.Kind,
            CharCount = a.Text.Length,
            Truncated = a.Truncated,
            PageCount = a.PageCount,
        };

        /// <summary>Removes one of the current user's uploads that has not been sent yet.</summary>
        [HttpDelete("attachments/{attachmentId:long}")]
        public async Task<IActionResult> DeleteAttachment(long attachmentId, CancellationToken ct = default)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var attachment = await _dbContext.ChatAttachments
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.UserId == userId && a.MessageId == null, ct);
            if (attachment == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Attachment not found.");

            _dbContext.ChatAttachments.Remove(attachment);
            await _dbContext.SaveChangesAsync(ct);
            await DeleteStoredFilesAsync([new StoredFile(attachment.BlobName, attachment.AnthropicFileId)], ct);

            return _responseFactory.CreateOKResponse();
        }

        /// <summary>
        /// The stored image or PDF of one of the current user's attachments, sent or not — what the
        /// chat shows as a thumbnail or opens when the attachment is clicked.
        /// </summary>
        [HttpGet("attachments/{attachmentId:long}/content")]
        public async Task<IActionResult> GetAttachmentContent(long attachmentId, CancellationToken ct = default)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var attachment = await _dbContext.ChatAttachments
                .Where(a => a.Id == attachmentId && a.UserId == userId && a.BlobName != null)
                .Select(a => new { a.BlobName, a.MediaType })
                .FirstOrDefaultAsync(ct);
            var content = attachment == null ? null : await _attachmentStore.ReadAsync(attachment.BlobName!, ct);
            if (content == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Attachment not found.");

            // Never changes once uploaded; private, since it is one user's file.
            Response.Headers.CacheControl = "private, max-age=86400";
            return File(content, attachment!.MediaType ?? "application/octet-stream");
        }

        /// <summary>
        /// Lists the current user's conversations (most-recently updated first).
        /// </summary>
        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations(CancellationToken ct = default)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var result = await _dbContext.ChatConversations
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new ChatConversationDto
                {
                    Id = c.Id,
                    Title = string.IsNullOrWhiteSpace(c.Title) ? "新對話" : c.Title!,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                })
                .ToListAsync(ct);

            return _responseFactory.CreateOKResponse(result);
        }

        /// <summary>
        /// Returns the message history of a conversation (oldest first) for the current user.
        /// </summary>
        [HttpGet("conversations/{conversationId}/messages")]
        public async Task<IActionResult> GetConversationMessages(string conversationId, CancellationToken ct = default)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");
            if (string.IsNullOrWhiteSpace(conversationId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "conversationId is required.");

            var owned = await _dbContext.ChatConversations
                .AnyAsync(c => c.Id == conversationId && c.UserId == userId, ct);
            if (!owned)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Conversation not found.");

            var result = await _dbContext.ChatMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new ChatMessageDto
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
                })
                .ToListAsync(ct);

            return _responseFactory.CreateOKResponse(result);
        }

        /// <summary>
        /// Stops the answer being written in one of the current user's conversations. What was said
        /// before the stop is kept, marked as cut off; the stream ends with done { stopped: true }.
        /// Succeeds whether or not an answer was still being written (it may just have finished).
        /// </summary>
        [HttpPost("conversations/{conversationId}/stop")]
        public IActionResult StopConversation(string conversationId)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var stopped = _turns.Stop(userId, conversationId);
            _logger.LogInformation("User {UserId} stopped conversation {ConversationId} (running: {Stopped})",
                userId, conversationId, stopped);
            return _responseFactory.CreateOKResponse(new { stopped });
        }

        /// <summary>Deletes one of the current user's conversations (and its messages).</summary>
        [HttpDelete("conversations/{conversationId}")]
        public async Task<IActionResult> DeleteConversation(string conversationId, CancellationToken ct = default)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");
            if (string.IsNullOrWhiteSpace(conversationId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "conversationId is required.");

            var conversation = await _dbContext.ChatConversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
            if (conversation == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Conversation not found.");

            // The rows cascade (messages, then their attachments); the stored files do not.
            var files = await StoredFiles(_dbContext.ChatAttachments
                .Where(a => a.Message!.ConversationId == conversationId)).ToListAsync(ct);

            _dbContext.ChatConversations.Remove(conversation); // cascades to ChatMessages
            await _dbContext.SaveChangesAsync(ct);
            await DeleteStoredFilesAsync(files, ct);

            return _responseFactory.CreateOKResponse();
        }
    }
}
