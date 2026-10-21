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
    /// Calls Claude (Haiku 5.5) directly with the TW-stock MCP server attached, replacing the
    /// previous Dify-hosted chatbot. Conversation history is stored locally (not on a third-party
    /// platform) and re-sent as context on every turn — the Messages API is stateless. Each stored
    /// turn goes back with its own role (user / assistant), but only as the text that was shown:
    /// the tool calls and the model's thinking from earlier turns are not stored or re-sent.
    /// </summary>
    public class AnthropicChatService(
        AzureSQLDBContext db,
        IChatAttachmentStore attachmentStore,
        IAiUsageLogger usageLogger,
        IOptionsMonitor<Settings> settings,
        ILogger<AnthropicChatService> logger,
        TimeProvider? timeProvider = null) : IAnthropicChatService
    {
        // The usage log's model name; must name the same model as the request below.
        private const string ModelId = "claude-haiku-5-5";
        private const string McpServerName = "tw-stock";
        private const string McpServerUrl = "https://TW-Stock-MCP-Server.fastmcp.app/mcp";
        private const string McpBeta = "mcp-client-2025-11-20";
        // Haiku 5.5 thinks by default (adaptive), and that thinking counts toward this cap along
        // with the MCP tool call and the analysis text. 2048 was once observed truncating
        // mid-analysis (empty final response) on requests like "分析一下2330", and thinking only
        // adds to what a turn needs. It is a ceiling, not a charge: a turn pays for what it uses.
        private const int MaxTokens = 32000;

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        // The MCP connector runs its tool-call loop server-side with an internal 10-iteration
        // cap; hitting it ends the turn with stop_reason=pause_turn before any answer text is
        // produced. Retrying the same request from scratch (not a true resume — see below) often
        // succeeds if the stall was a transient MCP-server hiccup. Bounded so a query that
        // genuinely needs an unusually large number of tool calls fails fast instead of looping.
        private const int MaxPauseTurnRetries = 2;

        /// <summary>
        /// Today's date goes in up front: the model's training data ends well before it, and
        /// without it a question about "today" or "the latest" is answered from that cutoff rather
        /// than looked up with the tool.
        /// </summary>
        internal static string SystemPromptFor(DateOnly taipeiToday) =>
            $"今天是 {taipeiToday:yyyy-MM-dd}（台北時間）。" +
            "你是JackyAIApp網站上的台股小幫手。有台股相關問題（股價、財報、籌碼等）時，使用tw-stock工具查詢最新資料再回答；" +
            "不確定或工具查無資料時要誠實告知，不要編造數字。只回答跟本網站功能或台股相關的問題，其他問題禮貌婉拒。用繁體中文，簡潔明確地回答。";

        /// <summary>
        /// Why a turn ended without any answer text. A decline from the model's safety classifiers
        /// (stop_reason=refusal; there is no server-side fallback for Haiku) is not the same failure
        /// as running out of room, and asking to "try again" would only get the same decline.
        /// </summary>
        internal static string NoAnswerCode(BetaStopReason? stopReason) =>
            stopReason == BetaStopReason.Refusal ? ChatErrorCodes.Refused : ChatErrorCodes.NoAnswer;

        public async Task<ChatStreamResult> StreamChatAsync(
            string userId, string message, string? conversationId, IReadOnlyCollection<SQL.ChatAttachment> attachments,
            HttpResponse response, CancellationToken ct)
        {
            var apiKey = settings.CurrentValue.AnthropicApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogError("Anthropic API key not configured");
                StartEventStream(response);
                await WriteSseAsync(response, new { type = "error", code = ChatErrorCodes.Unavailable }, ct);
                return new ChatStreamResult(false, conversationId ?? "", ChatErrorCodes.Unavailable);
            }

            var conversation = await ResolveConversationAsync(userId, conversationId, ct);

            var userMessage = new SQL.ChatMessage
            {
                ConversationId = conversation.Id,
                Role = "user",
                Content = message,
            };
            db.ChatMessages.Add(userMessage);
            // The attachments become part of this message, and of the conversation from here on.
            foreach (var attachment in attachments) attachment.Message = userMessage;
            await db.SaveChangesAsync(ct);

            var history = await db.ChatMessages
                .Where(m => m.ConversationId == conversation.Id)
                .Include(m => m.Attachments)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(ct);

            // Images and PDFs are files in the attachment store, read again for every turn they are
            // part of (the API keeps nothing between requests).
            var files = new Dictionary<long, byte[]>();
            foreach (var a in history.SelectMany(m => m.Attachments).Where(a => a.BlobName != null))
            {
                var content = await attachmentStore.ReadAsync(a.BlobName!, ct);
                if (content != null) files[a.Id] = content;
                else logger.LogWarning("Chat attachment {AttachmentId} has no stored file", a.Id);
            }

            var client = new AnthropicClient { ApiKey = apiKey };
            var requestMessages = history.Select(m => ToMessageParam(m, files)).ToList();

            var taipeiToday = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime.AddHours(8));
            var streamParams = new MessageCreateParams
            {
                Model = AnthropicModel.ClaudeHaiku5_5,
                MaxTokens = MaxTokens,
                // Medium is Haiku 5.5's default; set explicitly so a change of default upstream
                // cannot quietly change how much every turn thinks (and costs).
                OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                Betas = [McpBeta],
                McpServers = [new BetaRequestMcpServerUrlDefinition { Name = McpServerName, Url = McpServerUrl }],
                Tools = [new BetaMcpToolset { McpServerName = McpServerName }],
                System = SystemPromptFor(taipeiToday),
                Messages = requestMessages,
            };

            StartEventStream(response);

            var accumulated = new StringBuilder();
            var streamingStarted = false;
            long inputTokens = 0, outputTokens = 0;
            BetaStopReason? stopReason = null;
            var success = true;
            string? errorCode = null;

            for (var attempt = 0; ; attempt++)
            {
                stopReason = null;
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
                            inputTokens += startEvt.Message.Usage.InputTokens;
                        }
                        else if (evt.TryPickDelta(out var msgDelta))
                        {
                            if (msgDelta.Usage != null) outputTokens += msgDelta.Usage.OutputTokens;
                            if (msgDelta.Delta != null) stopReason = msgDelta.Delta.StopReason?.Value();
                        }
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    success = false;
                    errorCode = ChatErrorCodes.For(ex);
                    // The browser only gets the code; this is where the actual error is kept.
                    logger.LogError(ex,
                        "Anthropic chat streaming error for conversation {ConversationId}; reported as {ErrorCode}",
                        conversation.Id, errorCode);
                    break;
                }
                catch (OperationCanceledException)
                {
                    // The reader left (closed the page): nothing to tell anyone, just stop.
                    success = false;
                    break;
                }

                // pause_turn = the MCP connector's server-side tool loop hit its internal
                // iteration cap before producing any answer text. Nothing has reached the client
                // yet (streamingStarted is false), so a from-scratch retry is invisible to the
                // user — NOT a true mid-conversation resume (the SDK has no block-accumulation
                // helper to reconstruct the partial assistant turn), just "try the whole request
                // again," which is enough for the common case of a transient MCP hiccup.
                if (!streamingStarted && stopReason == BetaStopReason.PauseTurn && attempt < MaxPauseTurnRetries)
                {
                    logger.LogWarning(
                        "Anthropic chat turn hit pause_turn with no output for conversation {ConversationId}; retrying (attempt {Attempt}/{Max})",
                        conversation.Id, attempt + 1, MaxPauseTurnRetries);
                    continue;
                }

                break;
            }

            // The stream can complete with no exception yet produce zero text — e.g. the MCP
            // tool call plus follow-up analysis ran out of MaxTokens before any answer text was
            // written. Treat that as a failure rather than silently sending "done" with nothing
            // to show (previously left the chat bubble blank forever with no error surfaced).
            if (success && accumulated.Length == 0)
            {
                logger.LogWarning(
                    "Anthropic chat turn produced no text for conversation {ConversationId}. StopReason={StopReason}",
                    conversation.Id, stopReason?.ToString() ?? "(none)");
                success = false;
                errorCode = NoAnswerCode(stopReason);
            }

            // A decline can also arrive after some text has streamed. That text is a cut-off
            // answer, so it is reported as a failure and not kept as the assistant's turn.
            var refused = success && stopReason == BetaStopReason.Refusal;
            if (refused)
            {
                logger.LogWarning("Anthropic chat turn was declined mid-answer for conversation {ConversationId}", conversation.Id);
                success = false;
                errorCode = ChatErrorCodes.Refused;
            }

            // An answer that ran into the output cap is kept, but the reader is told it is cut short.
            var truncated = success && stopReason == BetaStopReason.MaxTokens;
            if (truncated)
                logger.LogWarning("Anthropic chat turn hit max_tokens for conversation {ConversationId}", conversation.Id);

            if (accumulated.Length > 0 && !refused)
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
                await WriteSseAsync(response, new { type = "done", conversationId = conversation.Id, truncated }, ct);
            else if (!ct.IsCancellationRequested)
                await WriteSseAsync(response, new { type = "error", code = errorCode ?? ChatErrorCodes.Failed }, ct);

            return new ChatStreamResult(streamingStarted, conversation.Id, errorCode);
        }

        /// <summary>
        /// One stored turn as the API takes it. A message with attachments becomes a list of blocks,
        /// attachments ahead of the question about them: a text file as a plain-text document and a
        /// PDF as a PDF document, each titled with its file name; an image as an image, after a line
        /// naming it (an image block has no title, and the reader may refer to it by name).
        /// </summary>
        /// <param name="files">The stored files of image and PDF attachments, by attachment id; one
        /// missing here is gone from the store, and the model is told so instead.</param>
        internal static BetaMessageParam ToMessageParam(SQL.ChatMessage m, IReadOnlyDictionary<long, byte[]> files)
        {
            var role = m.Role == "user" ? Role.User : Role.Assistant;
            if (m.Attachments.Count == 0)
                return new BetaMessageParam { Role = role, Content = m.Content };

            var blocks = new List<BetaContentBlockParam>();
            foreach (var a in m.Attachments.OrderBy(a => a.Id))
            {
                if (!AttachmentKinds.IsFile(a.Kind))
                {
                    blocks.Add(new BetaRequestDocumentBlock
                    {
                        Source = new BetaPlainTextSource { Data = a.Truncated ? a.Text + TruncationNote : a.Text },
                        Title = a.FileName,
                    });
                }
                else if (!files.TryGetValue(a.Id, out var content))
                {
                    blocks.Add(new BetaTextBlockParam { Text = string.Format(MissingFileNote, a.FileName) });
                }
                else if (a.Kind == AttachmentKinds.Pdf)
                {
                    blocks.Add(new BetaRequestDocumentBlock
                    {
                        Source = new BetaBase64PdfSource { Data = Convert.ToBase64String(content) },
                        Title = a.FileName,
                    });
                }
                else
                {
                    blocks.Add(new BetaTextBlockParam { Text = string.Format(ImageNameNote, a.FileName) });
                    blocks.Add(new BetaImageBlockParam
                    {
                        Source = new BetaBase64ImageSource
                        {
                            Data = Convert.ToBase64String(content),
                            MediaType = a.MediaType == "image/png"
                                ? Anthropic.Models.Beta.Messages.MediaType.ImagePng
                                : Anthropic.Models.Beta.Messages.MediaType.ImageJpeg,
                        },
                    });
                }
            }
            blocks.Add(new BetaTextBlockParam { Text = m.Content });
            return new BetaMessageParam { Role = role, Content = blocks };
        }

        /// <summary>Names the image that follows it.</summary>
        internal const string ImageNameNote = "[附件圖片：{0}]";

        /// <summary>Stands in for an image or PDF whose stored file is gone.</summary>
        internal const string MissingFileNote = "[附件「{0}」已無法讀取。]";

        /// <summary>Appended to a cut attachment, so the model does not answer as if it saw all of it.</summary>
        internal const string TruncationNote = "\n\n[此檔案內容過長，以上僅為前段，其餘已截斷。]";

        private static void StartEventStream(HttpResponse response)
        {
            response.ContentType = "text/event-stream";
            response.Headers.Append("Cache-Control", "no-cache");
            response.Headers.Append("Connection", "keep-alive");
            response.Headers.Append("Access-Control-Allow-Origin", "*");
            response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
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
