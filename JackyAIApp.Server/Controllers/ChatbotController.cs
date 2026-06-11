using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Dify;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController(
        ILogger<ChatbotController> logger,
        IMyResponseFactory responseFactory,
        IUserService userService,
        IDifyApiClient difyApiClient,
        ICreditService creditService) : ControllerBase
    {
        private readonly ILogger<ChatbotController> _logger = logger;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IUserService _userService = userService;
        private readonly IDifyApiClient _difyApiClient = difyApiClient;
        private readonly ICreditService _creditService = creditService;

        /// <summary>
        /// Forwards chat messages to Dify API and streams the response back to the client.
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

            // Charge per message up front (anti-abuse / cost control). Refunded below
            // if Dify never starts responding, so users aren't charged for nothing.
            if (!await _creditService.ConsumeCreditsAsync(
                    userId, CreditCosts.ChatMessage, "chatbot_message", "Chatbot conversation message"))
            {
                Response.StatusCode = 402; // Payment Required
                await Response.WriteAsync($"Insufficient credits. Each message costs {CreditCosts.ChatMessage} credits.");
                return;
            }

            var streamingStarted = false;
            try
            {
                using var difyResponse = await _difyApiClient.SendChatStreamAsync(
                    request.Message, request.ConversationId, userId, HttpContext.RequestAborted);

                if (!difyResponse.IsSuccessStatusCode)
                {
                    var errorContent = await difyResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Dify API error: {StatusCode}, {Error}", difyResponse.StatusCode, errorContent);
                    await RefundChatMessageAsync(userId);
                    Response.StatusCode = (int)difyResponse.StatusCode;
                    await Response.WriteAsync(errorContent);
                    return;
                }

                // Set SSE headers
                Response.ContentType = "text/event-stream";
                Response.Headers.Append("Cache-Control", "no-cache");
                Response.Headers.Append("Connection", "keep-alive");
                Response.Headers.Append("Access-Control-Allow-Origin", "*");
                Response.Headers.Append("Access-Control-Allow-Headers", "Cache-Control");

                var bufferingFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
                bufferingFeature?.DisableBuffering();

                using var difyStream = await difyResponse.Content.ReadAsStreamAsync();
                var buffer = new byte[1024];
                streamingStarted = true;

                while (true)
                {
                    var bytesRead = await difyStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    await Response.Body.WriteAsync(buffer, 0, bytesRead);
                    await Response.Body.FlushAsync();

                    if (HttpContext.RequestAborted.IsCancellationRequested)
                    {
                        _logger.LogInformation("Client disconnected from stream");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during chat streaming");
                if (!streamingStarted)
                {
                    await RefundChatMessageAsync(userId);
                    Response.StatusCode = 500;
                    await Response.WriteAsync("Internal server error");
                }
            }
        }

        /// <summary>Refunds a chat message charge when no answer was delivered.</summary>
        private Task RefundChatMessageAsync(string userId) =>
            _creditService.AddCreditsAsync(
                userId, CreditCosts.ChatMessage, "refund", "Chatbot message refund (no response delivered)");

        /// <summary>
        /// Lists the current user's Dify conversations (most-recently updated first).
        /// </summary>
        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations(CancellationToken ct = default)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var (ok, body) = await _difyApiClient.GetConversationsAsync(userId, ct);
            if (!ok)
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Failed to load conversations.");

            var parsed = JsonSerializer.Deserialize<DifyConversationListResponse>(body) ?? new();
            var result = parsed.Data
                .Select(c => new ChatConversationDto
                {
                    Id = c.Id,
                    Name = string.IsNullOrWhiteSpace(c.Name) ? "新對話" : c.Name!,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                })
                .ToList();
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

            var (ok, body) = await _difyApiClient.GetMessagesAsync(conversationId, userId, ct);
            if (!ok)
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Failed to load conversation messages.");

            var parsed = JsonSerializer.Deserialize<DifyMessageListResponse>(body) ?? new();
            var result = parsed.Data
                .OrderBy(m => m.CreatedAt)
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    Query = m.Query ?? string.Empty,
                    Answer = m.Answer ?? string.Empty,
                    CreatedAt = m.CreatedAt,
                })
                .ToList();
            return _responseFactory.CreateOKResponse(result);
        }

        /// <summary>Deletes one of the current user's conversations from Dify.</summary>
        [HttpDelete("conversations/{conversationId}")]
        public async Task<IActionResult> DeleteConversation(string conversationId, CancellationToken ct = default)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");
            if (string.IsNullOrWhiteSpace(conversationId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "conversationId is required.");

            var success = await _difyApiClient.DeleteConversationAsync(conversationId, userId, ct);
            if (!success)
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Failed to delete conversation.");

            return _responseFactory.CreateOKResponse();
        }
    }
}
