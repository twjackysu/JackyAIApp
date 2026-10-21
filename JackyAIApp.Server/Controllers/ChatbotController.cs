using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
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

            // Charge per message up front (anti-abuse / cost control). Refunded below if the
            // model never started responding, so users aren't charged for nothing.
            if (!await _usageLimitService.TryConsumeAsync(userId, UsageCosts.ChatMessage, "chatbot_message"))
            {
                Response.StatusCode = 429; // Too Many Requests
                await Response.WriteAsync("Daily usage limit exceeded.");
                return;
            }

            try
            {
                var result = await _chatService.StreamChatAsync(
                    userId, request.Message, request.ConversationId, Response, HttpContext.RequestAborted);

                // The service has already told the client why (an SSE error event with a code).
                if (!result.StreamingStarted)
                    await RefundChatMessageAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during chat streaming");
                if (!Response.HasStarted)
                {
                    await RefundChatMessageAsync(userId);
                    Response.StatusCode = 500;
                    await Response.WriteAsync("Internal server error");
                }
            }
        }

        /// <summary>Refunds a chat message charge when no answer was delivered.</summary>
        private Task RefundChatMessageAsync(string userId) =>
            _usageLimitService.RefundAsync(userId, UsageCosts.ChatMessage, "chatbot_message");

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
