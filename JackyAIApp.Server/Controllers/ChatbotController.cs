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
        AzureSQLDBContext dbContext) : ControllerBase
    {
        private readonly ILogger<ChatbotController> _logger = logger;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IUserService _userService = userService;
        private readonly IAnthropicChatService _chatService = chatService;
        private readonly IUsageLimitService _usageLimitService = usageLimitService;
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

            // Charge per message up front (anti-abuse / cost control), plus each attachment. Refunded
            // below if the model never started responding, so users aren't charged for nothing.
            var cost = UsageCosts.ChatMessage + UsageCosts.ChatAttachment * (ulong)attachments.Count;
            if (!await _usageLimitService.TryConsumeAsync(userId, cost, "chatbot_message"))
            {
                Response.StatusCode = 429; // Too Many Requests
                await Response.WriteAsync("Daily usage limit exceeded.");
                return;
            }

            try
            {
                var result = await _chatService.StreamChatAsync(
                    userId, request.Message, request.ConversationId, attachments, Response, HttpContext.RequestAborted);

                // The service has already told the client why (an SSE error event with a code).
                if (!result.StreamingStarted)
                    await RefundChatMessageAsync(userId, cost);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during chat streaming");
                if (!Response.HasStarted)
                {
                    await RefundChatMessageAsync(userId, cost);
                    Response.StatusCode = 500;
                    await Response.WriteAsync("Internal server error");
                }
            }
        }

        /// <summary>Refunds a chat message charge when no answer was delivered.</summary>
        private Task RefundChatMessageAsync(string userId, ulong cost) =>
            _usageLimitService.RefundAsync(userId, cost, "chatbot_message");

        /// <summary>Files sent with one message, and unsent uploads kept per user.</summary>
        internal const int MaxAttachmentsPerMessage = 5;

        /// <summary>An upload never sent is deleted once it is this old.</summary>
        internal static readonly TimeSpan UnsentAttachmentLifetime = TimeSpan.FromHours(24);

        /// <summary>
        /// Reads a Word, Excel, CSV or text file into text and keeps that text (not the file) until a
        /// chat message is sent with it. Free in itself: the charge is on the message that sends it.
        /// </summary>
        [HttpPost("attachments")]
        [RequestSizeLimit(AttachmentTextExtractor.MaxBytes + 64 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = AttachmentTextExtractor.MaxBytes + 64 * 1024)]
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
            if (file.Length > AttachmentTextExtractor.MaxBytes)
                return _responseFactory.CreateErrorResponse(ErrorCodes.PayloadTooLarge, "File is too large.");

            ExtractedAttachment extracted;
            try
            {
                await using var stream = file.OpenReadStream();
                extracted = AttachmentTextExtractor.Extract(kind, stream);
            }
            catch (UnreadableAttachmentException ex)
            {
                _logger.LogWarning(ex, "User {UserId} uploaded an unreadable {Kind} attachment", userId, kind);
                return _responseFactory.CreateErrorResponse(ErrorCodes.AttachmentUnreadable, "The file could not be read.");
            }
            if (string.IsNullOrWhiteSpace(extracted.Text))
                return _responseFactory.CreateErrorResponse(ErrorCodes.AttachmentUnreadable, "The file has no text.");

            // Uploads that were never sent (a message that failed, a tab that was closed) are not
            // kept: anything past a day old, and past the newest few, makes room for this one. The
            // client never holds more than one message's worth of unsent files.
            var staleBefore = DateTime.UtcNow - UnsentAttachmentLifetime;
            var keep = await _dbContext.ChatAttachments
                .Where(a => a.UserId == userId && a.MessageId == null && a.CreatedAt >= staleBefore)
                .OrderByDescending(a => a.Id)
                .Select(a => a.Id)
                .Take(MaxAttachmentsPerMessage - 1)
                .ToListAsync(ct);
            await _dbContext.ChatAttachments
                .Where(a => a.UserId == userId && a.MessageId == null && !keep.Contains(a.Id))
                .ExecuteDeleteAsync(ct);

            var attachment = new ChatAttachment
            {
                UserId = userId,
                FileName = fileName.Length > 260 ? fileName[^260..] : fileName,
                Kind = extracted.Kind,
                Text = extracted.Text,
                Truncated = extracted.Truncated,
            };
            _dbContext.ChatAttachments.Add(attachment);
            await _dbContext.SaveChangesAsync(ct);

            return _responseFactory.CreateOKResponse(new ChatAttachmentDto
            {
                Id = attachment.Id,
                FileName = attachment.FileName,
                Kind = attachment.Kind,
                CharCount = attachment.Text.Length,
                Truncated = attachment.Truncated,
            });
        }

        /// <summary>Removes one of the current user's uploads that has not been sent yet.</summary>
        [HttpDelete("attachments/{attachmentId:long}")]
        public async Task<IActionResult> DeleteAttachment(long attachmentId, CancellationToken ct = default)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var deleted = await _dbContext.ChatAttachments
                .Where(a => a.Id == attachmentId && a.UserId == userId && a.MessageId == null)
                .ExecuteDeleteAsync(ct);
            if (deleted == 0)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Attachment not found.");

            return _responseFactory.CreateOKResponse();
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
                        })
                        .ToList(),
                })
                .ToListAsync(ct);

            return _responseFactory.CreateOKResponse(result);
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

            _dbContext.ChatConversations.Remove(conversation); // cascades to ChatMessages
            await _dbContext.SaveChangesAsync(ct);

            return _responseFactory.CreateOKResponse();
        }
    }
}
