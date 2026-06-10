using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController(ILogger<ChatbotController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, IUserService userService, IHttpClientFactory httpClientFactory) : ControllerBase
    {
        private readonly ILogger<ChatbotController> _logger = logger;
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IUserService _userService = userService;
        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

        private const string DifyBaseUrl = "https://api.dify.ai/v1";

        /// <summary>
        /// Forwards chat messages to Dify API and streams the response back to the client.
        /// </summary>
        /// <param name="request">The chat request containing the user's message</param>
        /// <returns>A streaming response from Dify API</returns>
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
            _logger.LogInformation("User {UserId} started chat", userId);

            try
            {
                // 準備 Dify API 請求
                var difyRequest = new
                {
                    inputs = new { },
                    query = request.Message,
                    response_mode = "streaming",
                    conversation_id = request.ConversationId ?? "",
                    user = userId
                };

                var jsonContent = JsonSerializer.Serialize(difyRequest);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Dify Cloud API 設定
                var difyApiUrl = $"{DifyBaseUrl}/chat-messages";
                var difyApiKey = _settings.CurrentValue.DifyApiKey;
                
                _logger.LogInformation("Sending to Dify: {Request}", jsonContent);

                if (string.IsNullOrEmpty(difyApiKey))
                {
                    Response.StatusCode = 500;
                    await Response.WriteAsync("API key not configured");
                    return;
                }

                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {difyApiKey}");
                httpClient.DefaultRequestHeaders.Add("Accept", "text/event-stream");
                httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
                
                // 使用正確的 SendAsync 方法以支援 streaming
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, difyApiUrl)
                {
                    Content = content
                };
                using var difyResponse = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);

                if (!difyResponse.IsSuccessStatusCode)
                {
                    var errorContent = await difyResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Dify API error: {StatusCode}, {Error}", difyResponse.StatusCode, errorContent);
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
                
                // 禁用 buffering（重要！）
                var bufferingFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
                bufferingFeature?.DisableBuffering();
                
                // 直接透傳 chunk，就像 Node.js 的 res.write(chunk)
                using var difyStream = await difyResponse.Content.ReadAsStreamAsync();
                var buffer = new byte[1024];
                
                while (true)
                {
                    var bytesRead = await difyStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    
                    // 直接寫入原始 bytes，不做任何處理
                    await Response.Body.WriteAsync(buffer, 0, bytesRead);
                    await Response.Body.FlushAsync();
                    
                    // 檢查客戶端是否斷線
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
                Response.StatusCode = 500;
                await Response.WriteAsync("Internal server error");
            }
        }

        /// <summary>
        /// Lists the current user's Dify conversations (most-recently updated first).
        /// Titles are auto-generated by Dify; conversation content lives in Dify.
        /// </summary>
        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations(CancellationToken ct = default)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var url = $"{DifyBaseUrl}/conversations?user={Uri.EscapeDataString(userId)}&limit=100&sort_by=-updated_at";
            var (ok, body) = await SendDifyGetAsync(url, ct);
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

            var url = $"{DifyBaseUrl}/messages?conversation_id={Uri.EscapeDataString(conversationId)}&user={Uri.EscapeDataString(userId)}&limit=100";
            var (ok, body) = await SendDifyGetAsync(url, ct);
            if (!ok)
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Failed to load conversation messages.");

            var parsed = JsonSerializer.Deserialize<DifyMessageListResponse>(body) ?? new();
            var result = parsed.Data
                .OrderBy(m => m.CreatedAt) // oldest → newest for top-to-bottom rendering
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

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.CurrentValue.DifyApiKey);

            var request = new HttpRequestMessage(HttpMethod.Delete,
                $"{DifyBaseUrl}/conversations/{Uri.EscapeDataString(conversationId)}")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { user = userId }), Encoding.UTF8, "application/json"),
            };

            var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Dify delete conversation failed: {Status} {Error}", response.StatusCode, err);
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Failed to delete conversation.");
            }
            return _responseFactory.CreateOKResponse();
        }

        /// <summary>Performs an authenticated GET against the Dify Service API.</summary>
        private async Task<(bool ok, string body)> SendDifyGetAsync(string url, CancellationToken ct)
        {
            var apiKey = _settings.CurrentValue.DifyApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("Dify API key not configured");
                return (false, string.Empty);
            }

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                var response = await httpClient.GetAsync(url, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Dify GET failed: {Status} {Body}", response.StatusCode, body);
                    return (false, body);
                }
                return (true, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dify GET request error for {Url}", url);
                return (false, string.Empty);
            }
        }
    }

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? ConversationId { get; set; }
    }

    /// <summary>A conversation summary returned to the client (title from Dify).</summary>
    public class ChatConversationDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }
    }

    /// <summary>One historical exchange (user query + assistant answer).</summary>
    public class ChatMessageDto
    {
        public string Id { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public long CreatedAt { get; set; }
    }
}