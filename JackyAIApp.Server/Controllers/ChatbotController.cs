using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Assistant;
using JackyAIApp.Server.Services.Chatbot;
using JackyAIApp.Server.Services.Finance.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController(
        ILogger<ChatbotController> logger,
        IMyResponseFactory responseFactory,
        ICurrentUser currentUser,
        IAnthropicChatService chatService,
        IUsageLimitService usageLimitService,
        UsageLedger usageLedger,
        IChatAttachmentService attachments,
        IChatConversationService conversations,
        IChatProfiles profiles,
        ChatTurnRegistry turns) : ControllerBase
    {
        private readonly IChatProfiles _profiles = profiles;
        private readonly ChatTurnRegistry _turns = turns;
        private readonly IChatAttachmentService _attachments = attachments;
        private readonly IChatConversationService _conversations = conversations;
        private readonly ILogger<ChatbotController> _logger = logger;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly ICurrentUser _currentUser = currentUser;
        private readonly IAnthropicChatService _chatService = chatService;
        private readonly IUsageLimitService _usageLimitService = usageLimitService;
        private readonly UsageLedger _usageLedger = usageLedger;

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

            // Before any charge: a message for no assistant is not answered.
            if (_profiles.Find(request.Profile) == null)
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("Unknown assistant.");
                return;
            }

            var userId = _currentUser.RequireId();
            _logger.LogInformation("User {UserId} started chat with {Profile}", userId, request.Profile);

            var attachments = await _attachments.TakeUnsentAsync(request.AttachmentIds, HttpContext.RequestAborted);

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
                : await _attachments.SentWithAsync(editId.Value, HttpContext.RequestAborted);

            // Charge per message up front (anti-abuse / cost control), plus each attachment. Charged
            // here rather than through [UsageLimit] because the cost depends on the attachments.
            // Whether it is refunded is UsageLedger's rule, like every other charge: the stream has
            // sent 200 before it can fail, so a failure is reported to the ledger below.
            var cost = UsageCosts.ForChatMessage(attachments.Concat(editAttachments).Select(a => a.PageCount));
            if (!await _usageLimitService.TryConsumeAsync(userId, UsageFeature.ChatMessage, cost))
            {
                Response.StatusCode = 429; // Too Many Requests
                await Response.WriteAsync("Daily usage limit exceeded.");
                return;
            }

            // Only once charged: a refused message must not have cut the conversation short.
            if (editId != null)
            {
                var kept = await _conversations.RewindForEditAsync(
                    request.ConversationId!, editId.Value, request.Profile, HttpContext.RequestAborted);
                if (kept == null)
                {
                    // Refunded at the end of the request: a 404 with no AI call.
                    Response.StatusCode = 404;
                    await Response.WriteAsync("Message not found.");
                    return;
                }
                attachments = [.. kept.Concat(attachments).Take(ChatAttachmentLimits.MaxPerMessage)];
            }

            try
            {
                var result = await _chatService.StreamChatAsync(
                    userId, request.Profile, request.Message, request.ConversationId, attachments, Response, HttpContext.RequestAborted);

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

        /// <summary>
        /// Readies a file to be sent with the next chat message (<see cref="IChatAttachmentService.UploadAsync"/>).
        /// Free in itself: the charge is on the message that sends it.
        /// </summary>
        [HttpPost("attachments")]
        [RequestSizeLimit(ChatAttachmentLimits.MaxUploadBytes + 64 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = ChatAttachmentLimits.MaxUploadBytes + 64 * 1024)]
        public async Task<IActionResult> UploadAttachment(IFormFile? file, CancellationToken ct = default)
        {
            if (file == null || file.Length == 0)
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "A file is required.");

            await using var content = file.OpenReadStream();
            return _responseFactory.ToActionResult(await _attachments.UploadAsync(file.FileName, file.Length, content, ct));
        }

        /// <summary>Removes one of the current user's uploads that has not been sent yet.</summary>
        [HttpDelete("attachments/{attachmentId:long}")]
        public async Task<IActionResult> DeleteAttachment(long attachmentId, CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _attachments.DeleteUnsentAsync(attachmentId, ct));

        /// <summary>
        /// The stored image or PDF of one of the current user's attachments, sent or not — what the
        /// chat shows as a thumbnail or opens when the attachment is clicked.
        /// </summary>
        [HttpGet("attachments/{attachmentId:long}/content")]
        public async Task<IActionResult> GetAttachmentContent(long attachmentId, CancellationToken ct = default)
        {
            var result = await _attachments.GetContentAsync(attachmentId, ct);
            if (!result.IsSuccess)
                return _responseFactory.ToActionResult(result);

            // Never changes once uploaded; private, since it is one user's file.
            Response.Headers.CacheControl = "private, max-age=86400";
            return File(result.Data!.Bytes, result.Data.MediaType);
        }

        /// <summary>
        /// Lists the current user's conversations with one assistant (most-recently updated first);
        /// Finance's when none is named, as the /finance page asked before there were others.
        /// </summary>
        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations(
            [FromQuery] string profile = FinanceChatProfile.ProfileKey, CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _conversations.ListAsync(profile, ct));

        /// <summary>
        /// Returns the message history of a conversation (oldest first) for the current user.
        /// </summary>
        [HttpGet("conversations/{conversationId}/messages")]
        public async Task<IActionResult> GetConversationMessages(string conversationId, CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _conversations.GetMessagesAsync(conversationId, ct));

        /// <summary>
        /// Stops the answer being written in one of the current user's conversations. What was said
        /// before the stop is kept, marked as cut off; the stream ends with done { stopped: true }.
        /// Succeeds whether or not an answer was still being written (it may just have finished).
        /// </summary>
        [HttpPost("conversations/{conversationId}/stop")]
        public IActionResult StopConversation(string conversationId)
        {
            var userId = _currentUser.RequireId();
            var stopped = _turns.Stop(userId, conversationId);
            _logger.LogInformation("User {UserId} stopped conversation {ConversationId} (running: {Stopped})",
                userId, conversationId, stopped);
            return _responseFactory.CreateOKResponse(new ChatStopResponse(stopped));
        }

        /// <summary>Deletes one of the current user's conversations (and its messages).</summary>
        [HttpDelete("conversations/{conversationId}")]
        public async Task<IActionResult> DeleteConversation(string conversationId, CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _conversations.DeleteAsync(conversationId, ct));
    }
}
