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
        IConversationTitler titler,
        IChatProfiles profiles,
        ChatTurnRegistry turns,
        ChatApprovalRegistry approvals) : ControllerBase
    {
        private readonly ChatApprovalRegistry _approvals = approvals;
        private readonly IChatProfiles _profiles = profiles;
        private readonly ChatTurnRegistry _turns = turns;
        private readonly IChatAttachmentService _attachments = attachments;
        private readonly IChatConversationService _conversations = conversations;
        private readonly IConversationTitler _titler = titler;
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
        [RequestSizeLimit(MaxChatRequestBytes)]
        public async Task Chat([FromBody] ChatRequest request)
        {
            // Before any charge, so a refused message is neither charged nor stored.
            if (MessageRefusal(request) is { } refusal)
            {
                Response.StatusCode = 400;
                await Response.WriteAsync(refusal);
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
                    userId, request.Profile, request.Message, request.ConversationId, attachments,
                    PageContext(request.Context), request.QuotesPageText, Response, HttpContext.RequestAborted);

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
        /// Longest message, in UTF-16 code units as the client counts them. A message is stored before the
        /// model is asked (and kept when the answer fails, which is refunded), and sent again with every later
        /// turn, so without a limit one could fill the database or make a conversation that can never be
        /// answered again. A longer text goes as an attachment, which is read and cut to a size of its own.
        /// </summary>
        public const int MaxMessageLength = 20_000;

        /// <summary>Largest chat request body: a message at its limit with every character escaped, and the rest.</summary>
        private const long MaxChatRequestBytes = 1024 * 1024;

        /// <summary>Why a message is not taken, or null when it is.</summary>
        internal static string? MessageRefusal(ChatRequest? request) =>
            request == null || string.IsNullOrEmpty(request.Message) ? "Message cannot be null or empty."
            : request.Message.Length > MaxMessageLength ? "Message is too long."
            : null;

        /// <summary>The page's context as a turn takes it: at most 8 entries, keys of 40 characters and values of 200.</summary>
        internal static IReadOnlyDictionary<string, string> PageContext(Dictionary<string, string>? context) =>
            context == null
                ? new Dictionary<string, string>()
                : context
                    .Where(p => p.Key.Length is > 0 and <= 40 && p.Value != null && p.Value.Length <= 200)
                    .Take(8)
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

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
        /// Names one of the current user's conversations from its first question and answer (D-141): asked
        /// by the client once, after the first answer. Its title as it is when already named, or when
        /// naming fails. Not charged: part of the first message's cost.
        /// </summary>
        [HttpPost("conversations/{conversationId}/title")]
        public async Task<IActionResult> NameConversation(string conversationId, CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _titler.NameAsync(conversationId, ct));

        /// <summary>
        /// Gives one of the current user's conversations the title they typed (D-144). Not charged: no AI.
        /// A conversation renamed before the AI named it is not named by the AI after.
        /// </summary>
        [HttpPut("conversations/{conversationId}/title")]
        public async Task<IActionResult> RenameConversation(
            string conversationId, [FromBody] ChatRenameRequest request, CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _conversations.RenameAsync(conversationId, request.Title, ct));

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

        /// <summary>
        /// The reader's decisions on the writes an answer in one of their conversations is waiting for
        /// (<c>approval_required</c>); the answer carries on in its own stream. NotFound when nothing of
        /// theirs is waiting under that id: it was decided already, or timed out, or the turn is over.
        /// </summary>
        [HttpPost("conversations/{conversationId}/approvals/{pendingId}")]
        public IActionResult DecideApproval(string conversationId, string pendingId, [FromBody] ChatApprovalRequest request)
        {
            var userId = _currentUser.RequireId();
            var decisions = request.Decisions.Select(d => new ChatApprovalDecision(d.ToolUseId, d.Approve)).ToList();
            ServiceResult<ChatApprovalResponse> result = _approvals.Resolve(userId, conversationId, pendingId, decisions)
                ? ServiceResult.Ok(new ChatApprovalResponse(true))
                : ServiceResult.Fail(ErrorCodes.NotFound, "這個確認已經處理過或已逾時。");
            _logger.LogInformation("User {UserId} decided approval {PendingId} in conversation {ConversationId} ({Approved}/{Count} allowed, accepted: {Accepted})",
                userId, pendingId, conversationId, decisions.Count(d => d.Approve), decisions.Count, result.IsSuccess);
            return _responseFactory.ToActionResult(result);
        }

        /// <summary>Deletes one of the current user's conversations (and its messages).</summary>
        [HttpDelete("conversations/{conversationId}")]
        public async Task<IActionResult> DeleteConversation(string conversationId, CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _conversations.DeleteAsync(conversationId, ct));
    }
}
