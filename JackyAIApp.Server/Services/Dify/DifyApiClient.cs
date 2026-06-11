using JackyAIApp.Server.Configuration;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Dify
{
    public class DifyApiClient(
        IHttpClientFactory httpClientFactory,
        ILogger<DifyApiClient> logger,
        IOptionsMonitor<Settings> settings) : IDifyApiClient
    {
        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
        private readonly ILogger<DifyApiClient> _logger = logger;
        private readonly IOptionsMonitor<Settings> _settings = settings;

        private const string BaseUrl = "https://api.dify.ai/v1";

        private string ApiKey => _settings.CurrentValue.DifyApiKey ?? string.Empty;

        public async Task<HttpResponseMessage> SendChatStreamAsync(
            string message, string? conversationId, string userId, CancellationToken ct = default)
        {
            var payload = new
            {
                inputs = new { },
                query = message,
                response_mode = "streaming",
                conversation_id = conversationId ?? "",
                user = userId
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
            httpClient.DefaultRequestHeaders.Add("Accept", "text/event-stream");
            httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat-messages") { Content = content };

            // ResponseHeadersRead: don't buffer the body — stream it to the caller.
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        public Task<(bool ok, string body)> GetConversationsAsync(string userId, CancellationToken ct = default)
        {
            var url = $"{BaseUrl}/conversations?user={Uri.EscapeDataString(userId)}&limit=100&sort_by=-updated_at";
            return GetAsync(url, ct);
        }

        public Task<(bool ok, string body)> GetMessagesAsync(string conversationId, string userId, CancellationToken ct = default)
        {
            var url = $"{BaseUrl}/messages?conversation_id={Uri.EscapeDataString(conversationId)}&user={Uri.EscapeDataString(userId)}&limit=100";
            return GetAsync(url, ct);
        }

        public async Task<bool> DeleteConversationAsync(string conversationId, string userId, CancellationToken ct = default)
        {
            using var httpClient = CreateAuthorizedClient();
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/conversations/{Uri.EscapeDataString(conversationId)}")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { user = userId }), Encoding.UTF8, "application/json"),
            };

            try
            {
                using var response = await httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError("Dify delete conversation failed: {Status} {Error}", response.StatusCode, err);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dify DELETE request error for conversation {ConversationId}", conversationId);
                return false;
            }
        }

        private async Task<(bool ok, string body)> GetAsync(string url, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                _logger.LogError("Dify API key not configured");
                return (false, string.Empty);
            }

            using var httpClient = CreateAuthorizedClient();
            try
            {
                using var response = await httpClient.GetAsync(url, ct);
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

        private HttpClient CreateAuthorizedClient()
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            return client;
        }
    }
}
