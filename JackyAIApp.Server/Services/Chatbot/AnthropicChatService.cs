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
        IAnthropicFileStore anthropicFiles,
        ChatTurnRegistry turns,
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
        /// <remarks>
        /// The scope below follows what the tw-stock server can look up. It mostly decides which
        /// questions are in scope — the tools' own descriptions say how to use them.
        /// </remarks>
        internal static string SystemPromptFor(DateOnly taipeiToday) =>
            $"今天是 {taipeiToday:yyyy-MM-dd}（台北時間）。\n" +
            "你是JackyAIApp網站上的台股投資助手，用tw-stock工具查詢台灣市場的資料再回答。可查的範圍：\n" +
            "- 個股（上市、上櫃、興櫃）：股價與技術面、財報（損益、資產負債、現金流量，可回溯多期）、月營收、股利與除權息、法說會、重大訊息、董監與內部人持股、庫藏股\n" +
            "- 籌碼：三大法人與外資動向、融資融券、集保股權分散（大戶與散戶）、當沖、處置與注意股\n" +
            "- ETF：基本資料、折溢價、規模與受益人數、申購買回、成分股\n" +
            "- 期貨與選擇權：行情（可回溯，含盤中走勢）、三大法人與大額交易人部位、Put/Call Ratio\n" +
            "- 總體經濟：景氣燈號與領先、同時、落後指標、PMI／NMI、匯率\n" +
            "回答原則：數字以工具查到的為準，並註明資料日期；查無資料或不確定時誠實說明，不要編造。" +
            "使用者附上的檔案（報告、截圖、表格）可以一起分析。給出買賣方向的看法時，註明僅供參考、不構成投資建議。" +
            "只回答台灣金融市場、投資理財或本網站功能相關的問題，其他問題禮貌婉拒。用繁體中文，簡潔明確地回答。";

        /// <summary>
        /// Why a turn ended without any answer text. A decline from the model's safety classifiers
        /// (stop_reason=refusal; there is no server-side fallback for Haiku) is not the same failure
        /// as running out of room, and asking to "try again" would only get the same decline.
        /// </summary>
        internal static string NoAnswerCode(BetaStopReason? stopReason) =>
            stopReason == BetaStopReason.Refusal ? ChatErrorCodes.Refused : ChatErrorCodes.NoAnswer;

        /// <summary>
        /// The request for one turn. The whole conversation, attachments included, is sent again on
        /// every turn, so it is cached: the top-level cache_control puts the breakpoint on the last
        /// block, and the next turn — the same prefix with its new messages after — reads everything
        /// up to there at a tenth of the input price instead of paying for it again. The default
        /// 5-minute TTL: a reader's next message usually comes within minutes, each read restarts
        /// the 5 minutes, and a miss costs only a quarter more (the 1-hour TTL doubles every write).
        /// For the prefix to match, everything ahead of the conversation must stay byte-identical
        /// between turns: model, effort, tools and system prompt (which changes once a day, with
        /// the date), and each stored turn must be rebuilt exactly as it was sent.
        /// </summary>
        internal static MessageCreateParams BuildRequest(DateOnly taipeiToday, List<BetaMessageParam> messages) => new()
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
            Messages = messages,
            CacheControl = new BetaCacheControlEphemeral(),
        };

        /// <summary>
        /// Readies the conversation's images and PDFs to be sent. One already in the Files API is
        /// sent by its file_id and needs nothing; any other (uploaded before the Files API was used,
        /// or whose upload failed) is uploaded now from the attachment store and keeps its new
        /// file_id. One that still cannot be uploaded goes as its bytes, returned here by attachment
        /// id. With <paramref name="reupload"/>, every file is uploaded again, replacing file_ids the
        /// Files API no longer resolves.
        /// </summary>
        private async Task<Dictionary<long, byte[]>> PrepareFilesAsync(
            List<SQL.ChatMessage> history, bool reupload, CancellationToken ct)
        {
            var bytes = new Dictionary<long, byte[]>();
            var replaced = new List<string>();
            var changed = false;
            foreach (var a in history.SelectMany(m => m.Attachments).Where(a => a.BlobName != null))
            {
                if (a.AnthropicFileId != null && !reupload) continue;

                var content = await attachmentStore.ReadAsync(a.BlobName!, ct);
                if (content == null)
                {
                    logger.LogWarning("Chat attachment {AttachmentId} has no stored file", a.Id);
                    continue;
                }
                var fileId = await anthropicFiles.UploadAsync(content, a.MediaType ?? "application/octet-stream", ct);
                if (a.AnthropicFileId != null) replaced.Add(a.AnthropicFileId);
                if (fileId != a.AnthropicFileId)
                {
                    a.AnthropicFileId = fileId;
                    changed = true;
                }
                if (fileId == null) bytes[a.Id] = content;
            }
            if (changed) await db.SaveChangesAsync(ct);
            // Ids being replaced may still exist (a failure other than a missing file); not left behind.
            if (replaced.Count > 0) await anthropicFiles.DeleteAsync(replaced, ct);
            return bytes;
        }

        /// <summary>One request's token usage as the stream reports it, the latest report winning.</summary>
        private sealed class TurnUsage
        {
            public long Input, CacheWrite, CacheRead, Output;

            public void Update(long? input, long? cacheWrite, long? cacheRead, long? output)
            {
                if (input.HasValue) Input = input.Value;
                if (cacheWrite.HasValue) CacheWrite = cacheWrite.Value;
                if (cacheRead.HasValue) CacheRead = cacheRead.Value;
                if (output.HasValue) Output = output.Value;
            }
        }

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

            var files = await PrepareFilesAsync(history, reupload: false, ct);

            var client = new AnthropicClient { ApiKey = apiKey };
            var taipeiToday = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime.AddHours(8));
            var streamParams = BuildRequest(taipeiToday, history.Select(m => ToMessageParam(m, files)).ToList());
            var filesReuploaded = false;

            // The turn ends early if the reader stops it (POST conversations/{id}/stop) or leaves.
            using var turn = turns.Begin(userId, conversation.Id);
            using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct, turn.Token);
            var turnCt = turnCts.Token;
            var stopped = false;

            StartEventStream(response);
            // First, so the reader can stop even the first turn of a new conversation.
            try
            {
                await WriteSseAsync(response, new { type = "start", conversationId = conversation.Id, userMessageId = userMessage.Id }, turnCt);
            }
            catch (OperationCanceledException)
            {
                // Stopped already: the request below sees it straight away.
            }

            var accumulated = new StringBuilder();
            var streamingStarted = false;
            // Totals over every attempt (a pause_turn retry is a request of its own). Input is all of
            // it: uncached plus written to and read from the cache.
            long inputTokens = 0, outputTokens = 0, cacheWriteTokens = 0, cacheReadTokens = 0;
            BetaStopReason? stopReason = null;
            var success = true;
            string? errorCode = null;

            for (var attempt = 0; ; attempt++)
            {
                stopReason = null;
                // message_start gives this request's usage so far; the final message_delta gives its
                // cumulative usage, which also counts the MCP connector's server-side tool calls.
                var usage = new TurnUsage();
                try
                {
                    await foreach (var evt in client.Beta.Messages.CreateStreaming(streamParams, turnCt))
                    {
                        if (evt.TryPickContentBlockDelta(out var blockDelta) && blockDelta.Delta.TryPickText(out var textDelta))
                        {
                            streamingStarted = true;
                            accumulated.Append(textDelta.Text);
                            await WriteSseAsync(response, new { type = "delta", text = textDelta.Text }, turnCt);
                        }
                        else if (evt.TryPickContentBlockStart(out var blockStart) &&
                                 blockStart.ContentBlock.TryPickBetaMcpToolUse(out var toolUse))
                        {
                            await WriteSseAsync(response, new { type = "tool_use", name = toolUse.Name }, turnCt);
                        }
                        else if (evt.TryPickStart(out var startEvt))
                        {
                            var u = startEvt.Message.Usage;
                            usage.Update(u.InputTokens, u.CacheCreationInputTokens, u.CacheReadInputTokens, u.OutputTokens);
                        }
                        else if (evt.TryPickDelta(out var msgDelta))
                        {
                            if (msgDelta.Usage is { } u)
                                usage.Update(u.InputTokens, u.CacheCreationInputTokens, u.CacheReadInputTokens, u.OutputTokens);
                            if (msgDelta.Delta != null) stopReason = msgDelta.Delta.StopReason?.Value();
                        }
                    }
                }
                catch (Exception ex) when (!turnCt.IsCancellationRequested && !streamingStarted && !filesReuploaded &&
                                           ChatErrorCodes.IsMissingFile(ex))
                {
                    // A file_id the Files API no longer has: upload this conversation's files again from
                    // the attachment store and try once more. Nothing has reached the reader yet.
                    logger.LogWarning(ex, "Anthropic did not find a file of conversation {ConversationId}; uploading its files again",
                        conversation.Id);
                    filesReuploaded = true;
                    files = await PrepareFilesAsync(history, reupload: true, turnCt);
                    streamParams = BuildRequest(taipeiToday, history.Select(m => ToMessageParam(m, files)).ToList());
                    continue;
                }
                catch (Exception ex) when (!turnCt.IsCancellationRequested)
                {
                    success = false;
                    errorCode = ChatErrorCodes.For(ex);
                    // The browser only gets the code; this is where the actual error is kept.
                    logger.LogError(ex,
                        "Anthropic chat streaming error for conversation {ConversationId}; reported as {ErrorCode}",
                        conversation.Id, errorCode);
                    break;
                }
                catch (Exception) when (turnCt.IsCancellationRequested)
                {
                    // The reader stopped the turn, or left. Whatever the SDK threw on the way out (it
                    // need not be an OperationCanceledException), the turn ends here.
                    stopped = true;
                    success = false;
                    break;
                }
                finally
                {
                    inputTokens += usage.Input + usage.CacheWrite + usage.CacheRead;
                    cacheWriteTokens += usage.CacheWrite;
                    cacheReadTokens += usage.CacheRead;
                    outputTokens += usage.Output;
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

            // A stopped turn keeps what was said before the stop, marked as cut off — even if that is
            // nothing — so the reader sees it on reopening the conversation, and the model knows on the
            // next turn that this answer did not finish (as Claude Code records an interrupted turn).
            SQL.ChatMessage? assistantMessage = null;
            if (stopped || (accumulated.Length > 0 && !refused))
            {
                assistantMessage = new SQL.ChatMessage
                {
                    ConversationId = conversation.Id,
                    Role = "assistant",
                    Content = stopped ? StoppedContent(accumulated.ToString()) : accumulated.ToString(),
                };
                db.ChatMessages.Add(assistantMessage);

                conversation.UpdatedAt = DateTime.UtcNow;
                conversation.Title ??= TitleFrom(message);
                // Not the request's token: a stopped or abandoned turn is still saved.
                await db.SaveChangesAsync(CancellationToken.None);
            }

            // Cache reads that stay at zero turn after turn mean something in the prompt keeps
            // changing ahead of the cached part (see BuildRequest).
            logger.LogInformation(
                "Chat turn for conversation {ConversationId}: {InputTokens} input tokens ({CacheReadTokens} read from cache, {CacheWriteTokens} written), {OutputTokens} output",
                conversation.Id, inputTokens, cacheReadTokens, cacheWriteTokens, outputTokens);
            await usageLogger.LogChatAsync("chatbot.tw_stock", ModelId, (int)inputTokens, (int)outputTokens, success, userId,
                (int)cacheWriteTokens, (int)cacheReadTokens);

            if (success)
                await WriteSseAsync(response, new { type = "done", conversationId = conversation.Id, truncated, assistantMessageId = assistantMessage?.Id }, ct);
            else if (stopped && !ct.IsCancellationRequested)
                // Stopped through the API while the reader is still listening.
                await WriteSseAsync(response, new { type = "done", conversationId = conversation.Id, stopped = true, assistantMessageId = assistantMessage?.Id }, ct);
            else if (!ct.IsCancellationRequested)
                await WriteSseAsync(response, new { type = "error", code = errorCode ?? ChatErrorCodes.Failed }, ct);

            return new ChatStreamResult(streamingStarted, conversation.Id, errorCode);
        }

        /// <summary>Ends an answer the reader stopped; shown as it is, and read by the model as it is.</summary>
        internal const string StoppedNote = "（已中斷）";

        /// <summary>A stopped answer as stored: what was said before the stop, then <see cref="StoppedNote"/>.</summary>
        internal static string StoppedContent(string partial) =>
            string.IsNullOrWhiteSpace(partial) ? StoppedNote : $"{partial.TrimEnd()}\n\n{StoppedNote}";

        /// <summary>
        /// One stored turn as the API takes it. A message with attachments becomes a list of blocks,
        /// attachments ahead of the question about them: a text file as a plain-text document and a
        /// PDF as a PDF document, each titled with its file name; an image as an image, after a line
        /// naming it (an image block has no title, and the reader may refer to it by name).
        /// </summary>
        /// <param name="files">The bytes of the image and PDF attachments not in the Files API, by
        /// attachment id. An image or PDF with an <see cref="SQL.ChatAttachment.AnthropicFileId"/> is
        /// referred to by it; one with neither is gone, and the model is told so instead.</param>
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
                    continue;
                }

                var fileId = a.AnthropicFileId;
                byte[]? content = null;
                if (fileId == null && !files.TryGetValue(a.Id, out content))
                {
                    blocks.Add(new BetaTextBlockParam { Text = string.Format(MissingFileNote, a.FileName) });
                }
                else if (a.Kind == AttachmentKinds.Pdf)
                {
                    blocks.Add(new BetaRequestDocumentBlock
                    {
                        Source = fileId != null
                            ? new BetaFileDocumentSource { FileID = fileId }
                            : new BetaBase64PdfSource { Data = Convert.ToBase64String(content!) },
                        Title = a.FileName,
                    });
                }
                else
                {
                    blocks.Add(new BetaTextBlockParam { Text = string.Format(ImageNameNote, a.FileName) });
                    blocks.Add(new BetaImageBlockParam
                    {
                        Source = fileId != null
                            ? new BetaFileImageSource { FileID = fileId }
                            : new BetaBase64ImageSource
                            {
                                Data = Convert.ToBase64String(content!),
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
