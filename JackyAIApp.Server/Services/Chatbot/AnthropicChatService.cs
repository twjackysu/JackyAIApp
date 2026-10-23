using JackyAIApp.Server.Common;
using Anthropic.Models.Beta.Messages;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.AiUsage;
using JackyAIApp.Server.Services.Connectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
        IMarketBackground marketBackground,
        IAiUsageLogger usageLogger,
        UsageLedger usageLedger,
        IOptionsMonitor<Settings> settings,
        IAnthropicClientFactory clientFactory,
        ILogger<AnthropicChatService> logger,
        TimeProvider? timeProvider = null) : IAnthropicChatService
    {
        // The usage log's model name; must name the same model as the request below.
        private const string ModelId = "claude-haiku-5-5";
        private const string McpServerName = TwStockMcp.Name;
        private const string McpServerUrl = TwStockMcp.Url;
        private const string McpBeta = TwStockMcp.ConnectorBeta;
        // Haiku 5.5 thinks by default (adaptive), and that thinking counts toward this cap along
        // with the MCP tool call and the analysis text. 2048 was once observed truncating
        // mid-analysis (empty final response) on requests like "分析一下2330", and thinking only
        // adds to what a turn needs. It is a ceiling, not a charge: a turn pays for what it uses.
        private const int MaxTokens = 32000;

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        /// <summary>The tw-stock tools that are not deferred (<see cref="TwStockMcp.AlwaysLoadedTools"/>).</summary>
        internal static readonly string[] AlwaysLoadedTools = [.. TwStockMcp.AlwaysLoadedTools];

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
            "只回答台灣金融市場、投資理財或本網站功能相關的問題，其他問題禮貌婉拒。用繁體中文，簡潔明確地回答。\n" +
            // Unsure whether a tool it found can be called again, the model searched aimlessly
            // and once ended the turn with no answer.
            "工具採延遲載入：其他工具先用 tool_search_tool_regex 搜尋，pattern 可中英文關鍵字並用（例如「外資|institutional」）。" +
            "搜到的工具在整段對話中都能直接重複呼叫，不必再搜尋。\n" +
            // Every text delta reaches the reader, the notes between tool calls included; on a
            // retried tool call Haiku wrote those in English.
            "回覆中的所有文字，包括呼叫工具之間的說明，都用繁體中文。";

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
        /// the date, and with the market background every half hour — see IMarketBackground), and
        /// each stored turn must be rebuilt exactly as it was sent.
        /// <para>
        /// The market background, when there is one, is a second system block with its own
        /// breakpoint: it is the same for every reader until rebuilt, so tools and system prompt are
        /// cached once for all conversations, not each on its own.
        /// </para>
        /// </summary>
        internal static MessageCreateParams BuildRequest(
            DateOnly taipeiToday, List<BetaMessageParam> messages, string? marketBackground = null) => new()
        {
            Model = AnthropicModel.ClaudeHaiku5_5,
            MaxTokens = MaxTokens,
            // Medium is Haiku 5.5's default; set explicitly so a change of default upstream
            // cannot quietly change how much every turn thinks (and costs).
            OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
            Betas = [McpBeta],
            McpServers = [new BetaRequestMcpServerUrlDefinition { Name = McpServerName, Url = McpServerUrl }],
            // Regex, not BM25: BM25 found almost no tool for questions asked in Chinese. No
            // cache_control on a deferred tool (the API answers 400); the top-level breakpoint
            // below still caches the tools ahead of it.
            Tools =
            [
                new BetaToolSearchToolRegex20251119 { Type = BetaToolSearchToolRegex20251119Type.ToolSearchToolRegex20251119 },
                new BetaMcpToolset
                {
                    McpServerName = McpServerName,
                    DefaultConfig = new BetaMcpToolDefaultConfig { DeferLoading = true },
                    Configs = AlwaysLoadedTools.ToDictionary(name => name, _ => new BetaMcpToolConfig { DeferLoading = false }),
                },
            ],
            System = marketBackground == null
                ? SystemPromptFor(taipeiToday)
                : new List<BetaTextBlockParam>
                {
                    new() { Text = SystemPromptFor(taipeiToday) },
                    new() { Text = marketBackground, CacheControl = new BetaCacheControlEphemeral() },
                },
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

        public async Task<ChatStreamResult> StreamChatAsync(
            string userId, string message, string? conversationId, IReadOnlyCollection<SQL.ChatAttachment> attachments,
            HttpResponse response, CancellationToken ct)
        {
            var apiKey = settings.CurrentValue.AnthropicApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogError("Anthropic API key not configured");
                await SseStream.Open(response).WriteAsync(new { type = "error", code = ChatErrorCodes.Unavailable }, ct);
                return new ChatStreamResult(false, conversationId ?? "", ChatErrorCodes.Unavailable);
            }

            var (conversation, userMessage, history) = await StartTurnAsync(userId, message, conversationId, attachments, ct);
            var taipeiToday = _time.GetTaipeiToday();
            var background = await MarketBackgroundAsync(ct);
            Task<MessageCreateParams> RequestFor(bool reupload, CancellationToken token) =>
                RequestForAsync(history, taipeiToday, background, reupload, token);
            var request = await RequestFor(reupload: false, ct);

            // The turn ends early if the reader stops it (POST conversations/{id}/stop) or leaves.
            using var turn = turns.Begin(userId, conversation.Id);
            using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct, turn.Token);
            var sse = SseStream.Open(response);
            await WriteStartAsync(sse, conversation.Id, userMessage.Id, turnCts.Token);

            var outcome = await new ModelTurn(clientFactory.Create(apiKey), sse, usageLedger, logger, conversation.Id)
                .RunAsync(request, token => RequestFor(reupload: true, token), turnCts.Token);
            var verdict = TurnVerdict.Of(outcome);
            var answerId = await SaveAnswerAsync(conversation, message, verdict.Saved);
            await ReportTurnAsync(userId, conversation.Id, outcome, verdict);
            await EndStreamAsync(sse, conversation.Id, verdict, answerId, ct);
            return new ChatStreamResult(outcome.StreamingStarted, conversation.Id, verdict.ErrorCode);
        }

        /// <summary>
        /// The conversation the turn belongs to (a new one when none is given, or it is not this
        /// user's), with the user's message and its attachments saved into it, and its whole history.
        /// </summary>
        private async Task<(SQL.ChatConversation Conversation, SQL.ChatMessage UserMessage, List<SQL.ChatMessage> History)>
            StartTurnAsync(string userId, string message, string? conversationId,
                IReadOnlyCollection<SQL.ChatAttachment> attachments, CancellationToken ct)
        {
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
            return (conversation, userMessage, history);
        }

        /// <summary>The page's market data as background; the turn goes ahead without it if it cannot be had.</summary>
        private async Task<string?> MarketBackgroundAsync(CancellationToken ct)
        {
            try
            {
                return await marketBackground.GetAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Chat market background unavailable; answering without it");
                return null;
            }
        }

        /// <summary>The request for the conversation so far, its files readied first (see <see cref="PrepareFilesAsync"/>).</summary>
        private async Task<MessageCreateParams> RequestForAsync(
            List<SQL.ChatMessage> history, DateOnly taipeiToday, string? background, bool reupload, CancellationToken ct)
        {
            var files = await PrepareFilesAsync(history, reupload, ct);
            return BuildRequest(taipeiToday, history.Select(m => ToMessageParam(m, files)).ToList(), background);
        }

        /// <summary>First, so the reader can stop even the first turn of a new conversation.</summary>
        private static async Task WriteStartAsync(SseStream sse, string conversationId, long userMessageId, CancellationToken turnCt)
        {
            try
            {
                await sse.WriteAsync(new { type = "start", conversationId, userMessageId }, turnCt);
            }
            catch (OperationCanceledException)
            {
                // Stopped already: the request after this sees it straight away.
            }
        }

        /// <summary>Adds the answer the verdict keeps as the conversation's next message; its id, or null if none is kept.</summary>
        private async Task<long?> SaveAnswerAsync(SQL.ChatConversation conversation, string message, string? content)
        {
            if (content == null) return null;

            var answer = new SQL.ChatMessage
            {
                ConversationId = conversation.Id,
                Role = "assistant",
                Content = content,
            };
            db.ChatMessages.Add(answer);

            conversation.UpdatedAt = DateTime.UtcNow;
            conversation.Title ??= TitleFrom(message);
            // Not the request's token: a stopped or abandoned turn is still saved.
            await db.SaveChangesAsync(CancellationToken.None);
            return answer.Id;
        }

        /// <summary>The turn in the server log, and its tokens in the usage log.</summary>
        private async Task ReportTurnAsync(string userId, string conversationId, TurnOutcome outcome, TurnVerdict verdict)
        {
            if (verdict.Truncated)
                logger.LogWarning("Anthropic chat turn hit max_tokens for conversation {ConversationId}", conversationId);
            // Requests that went through but whose answer did not count: no text, or declined mid-answer.
            else if (!verdict.Success && !verdict.Stopped && outcome.ErrorCode == null)
            {
                if (outcome.Text.Length == 0)
                    logger.LogWarning(
                        "Anthropic chat turn produced no text for conversation {ConversationId}. StopReason={StopReason}",
                        conversationId, outcome.StopReason?.ToString() ?? "(none)");
                else
                    logger.LogWarning("Anthropic chat turn was declined mid-answer for conversation {ConversationId}", conversationId);
            }

            // Cache reads that stay at zero turn after turn mean something in the prompt keeps
            // changing ahead of the cached part (see BuildRequest). The tools called are what
            // AlwaysLoadedTools should follow.
            var tokens = outcome.Tokens;
            logger.LogInformation(
                "Chat turn for conversation {ConversationId}: {InputTokens} input tokens ({CacheReadTokens} read from cache, {CacheWriteTokens} written), {OutputTokens} output; tools called [{ToolsCalled}] after {ToolSearches} tool searches",
                conversationId, tokens.Input, tokens.CacheRead, tokens.CacheWrite, tokens.Output,
                string.Join(",", outcome.ToolsCalled), outcome.ToolSearches);
            await usageLogger.LogChatAsync("chatbot.tw_stock", ModelId, (int)tokens.Input, (int)tokens.Output, verdict.Success, userId,
                (int)tokens.CacheWrite, (int)tokens.CacheRead);
        }

        /// <summary>The turn's last event: done, or the error code. A reader who has left gets none.</summary>
        private static async Task EndStreamAsync(
            SseStream sse, string conversationId, TurnVerdict verdict, long? answerId, CancellationToken ct)
        {
            if (verdict.Success)
                await sse.WriteAsync(new { type = "done", conversationId, truncated = verdict.Truncated, assistantMessageId = answerId }, ct);
            else if (verdict.Stopped && !ct.IsCancellationRequested)
                // Stopped through the API while the reader is still listening.
                await sse.WriteAsync(new { type = "done", conversationId, stopped = true, assistantMessageId = answerId }, ct);
            else if (!ct.IsCancellationRequested)
                await sse.WriteAsync(new { type = "error", code = verdict.ErrorCode ?? ChatErrorCodes.Failed }, ct);
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
    }
}
