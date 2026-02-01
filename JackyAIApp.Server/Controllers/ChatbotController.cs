using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController(ILogger<ChatbotController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, IUserService userService, IHttpClientFactory httpClientFactory) : ControllerBase
    {
        private readonly ILogger<ChatbotController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly IUserService _userService = userService;
        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException();

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
                var difyApiUrl = "https://api.dify.ai/v1/chat-messages";
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

                // 設置正確的 SSE headers（參考 Node.js 版本）
                Response.ContentType = "text/event-stream";
                Response.Headers.Add("Cache-Control", "no-cache");
                Response.Headers.Add("Connection", "keep-alive");
                Response.Headers.Add("Access-Control-Allow-Origin", "*");
                Response.Headers.Add("Access-Control-Allow-Headers", "Cache-Control");
                
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

    }

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? ConversationId { get; set; }
    }
}