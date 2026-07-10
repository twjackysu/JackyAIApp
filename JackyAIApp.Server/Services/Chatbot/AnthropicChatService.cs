using Anthropic;
using Anthropic.Models.Beta.Messages;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.AiUsage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using AnthropicModel = Anthropic.Models.Messages.Model;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// Calls Claude (Haiku) directly with the TW-stock MCP server attached, replacing the previous
    /// Dify-hosted chatbot. Conversation history is stored locally (not on a third-party platform)
    /// and re-sent as context on every turn — the Messages API is stateless.
    /// </summary>
    public class AnthropicChatService(
        AzureSQLDBContext db,
        IAiUsageLogger usageLogger,
        IOptionsMonitor<Settings> settings,
        ILogger<AnthropicChatService> logger) : IAnthropicChatService
    {
        private const string ModelId = "claude-haiku-4-5";
        private const string McpServerName = "tw-stock";
        private const string McpServerUrl = "https://TW-Stock-MCP-Server.fastmcp.app/mcp";
        private const string McpBeta = "mcp-client-2025-11-20";
        private const int MaxTokens = 2048;

        private const string SystemPrompt =
            "你是JackyAIApp網站上的台股小幫手。有台股相關問題（股價、財報、籌碼等）時，使用tw-stock工具查詢最新資料再回答；" +
            "不確定或工具查無資料時要誠實告知，不要編造數字。只回答跟本網站功能或台股相關的問題，其他問題禮貌婉拒。用繁體中文，簡潔明確地回答。";

        public async Task<ChatStreamResult> StreamChatAsync(
            string userId, string message, string? conversationId, HttpResponse response, CancellationToken ct)
        {
            var apiKey = settings.CurrentValue.AnthropicApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogError("Anthropic API key not configured");
                return new ChatStreamResult(false, conversationId ?? "", "Chat service is not configured.");
            }

            var conversation = await ResolveConversationAsync(userId, conversationId, ct);

            db.ChatMessages.Add(new SQL.ChatMessage
            {
                ConversationId = conversation.Id,
                Role = "user",
                Content = message,
            });
            await db.SaveChangesAsync(ct);

            var history = await db.ChatMessages
                .Where(m => m.ConversationId == conversation.Id)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(ct);

            var client = new AnthropicClient { ApiKey = apiKey };
            var requestMessages = history
                .Select(m => new BetaMessageParam
                {
                    Role = m.Role == "user" ? Role.User : Role.Assistant,
                    Content = m.Content,
                })
                .ToList();

            var streamParams = new MessageCreateParams
            {
                Model = AnthropicModel.ClaudeHaiku4_5,
                MaxTokens = MaxTokens,
                Betas = [McpBeta],
                McpServers = [new BetaRequestMcpServerUrlDefinition { Name = McpServerName, Url = McpServerUrl }],
                Tools = [new BetaMcpToolset { McpServerName = McpServerName }],
                System = SystemPrompt,
                Messages = requestMessages,
            };

            response.ContentType = "text/event-stream";
            response.Headers.Append("Cache-Control", "no-cache");
            response.Headers.Append("Connection", "keep-alive");
            response.Headers.Append("Access-Control-Allow-Origin", "*");
            response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            var accumulated = new StringBuilder();
            var streamingStarted = false;
            long inputTokens = 0, outputTokens = 0;
            var success = true;
            string? errorMessage = null;

            try
            {
                await foreach (var evt in client.Beta.Messages.CreateStreaming(streamParams, ct))
                {
                    if (evt.TryPickContentBlockDelta(out var blockDelta) && blockDelta.Delta.TryPickText(out var textDelta))
                    {
                        streamingStarted = true;
                        accumulated.Append(textDelta.Text);
                        await WriteSseAsync(response, new { type = "delta", text = textDelta.Text }, ct);
                    }
                    else if (evt.TryPickContentBlockStart(out var blockStart) &&
                             blockStart.ContentBlock.TryPickBetaMcpToolUse(out var toolUse))
                    {
                        await WriteSseAsync(response, new { type = "tool_use", name = toolUse.Name }, ct);
                    }
                    else if (evt.TryPickStart(out var startEvt))
                    {
                        inputTokens = startEvt.Message.Usage.InputTokens;
                    }
                    else if (evt.TryPickDelta(out var msgDelta) && msgDelta.Usage != null)
                    {
                        outputTokens = msgDelta.Usage.OutputTokens;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Anthropic chat streaming error for conversation {ConversationId}", conversation.Id);
                success = false;
                errorMessage = "Chat request failed.";
                if (!streamingStarted)
                {
                    await usageLogger.LogChatAsync("chatbot.tw_stock", ModelId, (int)inputTokens, (int)outputTokens, success, userId);
                    return new ChatStreamResult(false, conversation.Id, errorMessage);
                }
                await WriteSseAsync(response, new { type = "error", message = errorMessage }, ct);
            }

            if (accumulated.Length > 0)
            {
                db.ChatMessages.Add(new SQL.ChatMessage
                {
                    ConversationId = conversation.Id,
                    Role = "assistant",
                    Content = accumulated.ToString(),
                });

                conversation.UpdatedAt = DateTime.UtcNow;
                conversation.Title ??= TitleFrom(message);
                await db.SaveChangesAsync(ct);
            }

            await usageLogger.LogChatAsync("chatbot.tw_stock", ModelId, (int)inputTokens, (int)outputTokens, success, userId);

            if (success)
            {
                await WriteSseAsync(response, new { type = "done", conversationId = conversation.Id }, ct);
            }

            return new ChatStreamResult(streamingStarted, conversation.Id, errorMessage);
        }

        private async Task<SQL.ChatConversation> ResolveConversationAsync(string userId, string? conversationId, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(conversationId))
            {
                var existing = await db.ChatConversations
                    .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
                if (existing != null) return existing;
            }

            var conversation = new SQL.ChatConversation { UserId = userId };
            db.ChatConversations.Add(conversation);
            await db.SaveChangesAsync(ct);
            return conversation;
        }

        private static string TitleFrom(string message) =>
            message.Length > 30 ? string.Concat(message.AsSpan(0, 30), "…") : message;

        private static async Task WriteSseAsync(HttpResponse response, object payload, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(payload);
            await response.WriteAsync($"data: {json}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }
}
