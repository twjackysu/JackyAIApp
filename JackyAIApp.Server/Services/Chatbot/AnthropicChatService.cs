using JackyAIApp.Server.Common;
using Anthropic.Models.Beta.Messages;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.AiUsage;
using JackyAIApp.Server.Services.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// The shared chat engine: calls Claude (Haiku 5.5) directly, with what the app's profile
    /// (<see cref="IChatProfile"/>) brings — its system prompt, background and tools (Finance's
    /// attaches the TW-stock MCP server) — replacing the previous Dify-hosted chatbot. Conversation
    /// history is stored locally (not on a third-party platform) and re-sent as context on every
    /// turn — the Messages API is stateless. Each stored
    /// turn goes back with its own role (user / assistant), but only as the text that was shown:
    /// the tool calls and the model's thinking from earlier turns are not stored or re-sent.
    /// </summary>
    public class AnthropicChatService(
        AzureSQLDBContext db,
        IChatAttachmentStore attachmentStore,
        IAnthropicFileStore anthropicFiles,
        ChatTurnRegistry turns,
        ChatApprovalRegistry approvals,
        IChatProfiles profiles,
        IAiUsageLogger usageLogger,
        UsageLedger usageLedger,
        IOptionsMonitor<Settings> settings,
        IAnthropicClientFactory clientFactory,
        ILogger<AnthropicChatService> logger,
        TimeProvider? timeProvider = null) : IAnthropicChatService
    {
        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        /// <summary>
        /// Why a turn ended without any answer text. A decline from the model's safety classifiers
        /// (stop_reason=refusal; there is no server-side fallback for Haiku) is not the same failure
        /// as running out of room, and asking to "try again" would only get the same decline.
        /// </summary>
        internal static string NoAnswerCode(BetaStopReason? stopReason) =>
            stopReason == BetaStopReason.Refusal ? ChatErrorCodes.Refused : ChatErrorCodes.NoAnswer;

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
            string userId, string profileKey, string message, string? conversationId, IReadOnlyCollection<SQL.ChatAttachment> attachments,
            IReadOnlyDictionary<string, string> pageContext, HttpResponse response, CancellationToken ct)
        {
            var apiKey = settings.CurrentValue.AnthropicApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogError("Anthropic API key not configured");
                await SseStream.Open(response).WriteAsync(new { type = "error", code = ChatErrorCodes.Unavailable }, ct);
                return new ChatStreamResult(false, conversationId ?? "", ChatErrorCodes.Unavailable);
            }

            var profile = profiles.Find(profileKey)
                ?? throw new ArgumentException($"No chat profile '{profileKey}'", nameof(profileKey));
            var (conversation, userMessage, history) = await StartTurnAsync(userId, profile.Key, message, conversationId, attachments, ct);
            var setup = await profile.SetupAsync(new ChatProfileContext(userId, _time.GetTaipeiToday(), pageContext), ct);
            Task<MessageCreateParams> RequestFor(bool reupload, CancellationToken token) =>
                RequestForAsync(history, setup, reupload, token);
            var request = await RequestFor(reupload: false, ct);

            // The turn ends early if the reader stops it (POST conversations/{id}/stop) or leaves.
            using var turn = turns.Begin(userId, conversation.Id);
            using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct, turn.Token);
            var sse = SseStream.Open(response);
            await WriteStartAsync(sse, conversation.Id, userMessage.Id, turnCts.Token);

            var tools = setup.Tools is { Count: > 0 }
                ? new ClientToolRunner(setup.Tools, sse, new AiToolContext(userId, conversation.Id), approvals, _time, logger)
                : null;
            var outcome = await new ModelTurn(clientFactory.Create(apiKey), sse, usageLedger, logger, conversation.Id, tools)
                .RunAsync(request, token => RequestFor(reupload: true, token), turnCts.Token);
            var verdict = TurnVerdict.Of(outcome);
            var answerId = await SaveAnswerAsync(conversation, message, verdict.Saved, outcome.Cards);
            await ReportTurnAsync(userId, profile, conversation.Id, outcome, verdict);
            await EndStreamAsync(sse, conversation.Id, verdict, answerId, ct);
            return new ChatStreamResult(outcome.StreamingStarted, conversation.Id, verdict.ErrorCode);
        }

        /// <summary>
        /// The conversation the turn belongs to (a new one when none is given, or it is not this
        /// user's with this assistant), with the user's message and its attachments saved into it, and
        /// its whole history.
        /// </summary>
        private async Task<(SQL.ChatConversation Conversation, SQL.ChatMessage UserMessage, List<SQL.ChatMessage> History)>
            StartTurnAsync(string userId, string profileKey, string message, string? conversationId,
                IReadOnlyCollection<SQL.ChatAttachment> attachments, CancellationToken ct)
        {
            var conversation = await ResolveConversationAsync(userId, profileKey, conversationId, ct);

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

        /// <summary>The request for the conversation so far, its files readied first (see <see cref="PrepareFilesAsync"/>).</summary>
        private async Task<MessageCreateParams> RequestForAsync(
            List<SQL.ChatMessage> history, ChatProfileSetup setup, bool reupload, CancellationToken ct)
        {
            var files = await PrepareFilesAsync(history, reupload, ct);
            return ChatTurnRequest.Build(setup, history.Select(m => ToMessageParam(m, files)).ToList());
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

        /// <summary>
        /// Adds the answer the verdict keeps as the conversation's next message, with the cards its
        /// tools showed; its id, or null if none is kept.
        /// </summary>
        private async Task<long?> SaveAnswerAsync(
            SQL.ChatConversation conversation, string message, string? content, IReadOnlyList<JsonElement>? cards)
        {
            if (content == null) return null;

            var answer = new SQL.ChatMessage
            {
                ConversationId = conversation.Id,
                Role = "assistant",
                Content = content,
                CardsJson = cards is { Count: > 0 } ? JsonSerializer.Serialize(cards) : null,
            };
            db.ChatMessages.Add(answer);

            conversation.UpdatedAt = DateTime.UtcNow;
            conversation.Title ??= TitleFrom(message);
            // Not the request's token: a stopped or abandoned turn is still saved.
            await db.SaveChangesAsync(CancellationToken.None);
            return answer.Id;
        }

        /// <summary>The turn in the server log, and its tokens in the usage log.</summary>
        private async Task ReportTurnAsync(
            string userId, IChatProfile profile, string conversationId, TurnOutcome outcome, TurnVerdict verdict)
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
            // changing ahead of the cached part (see ChatTurnRequest.Build). The tools called are what
            // TwStockMcp.AlwaysLoadedTools should follow.
            var tokens = outcome.Tokens;
            logger.LogInformation(
                "Chat turn for conversation {ConversationId}: {InputTokens} input tokens ({CacheReadTokens} read from cache, {CacheWriteTokens} written), {OutputTokens} output; tools called [{ToolsCalled}] after {ToolSearches} tool searches",
                conversationId, tokens.Input, tokens.CacheRead, tokens.CacheWrite, tokens.Output,
                string.Join(",", outcome.ToolsCalled), outcome.ToolSearches);
            await usageLogger.LogChatAsync(profile.UsageLogFeature, ChatTurnRequest.ModelId, (int)tokens.Input, (int)tokens.Output, verdict.Success, userId,
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

        /// <summary>
        /// The conversation to continue, or a new one. Another assistant's conversation is never
        /// continued: its history would be answered with this one's prompt and tools.
        /// </summary>
        private async Task<SQL.ChatConversation> ResolveConversationAsync(
            string userId, string profileKey, string? conversationId, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(conversationId))
            {
                var existing = await db.ChatConversations
                    .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && c.Profile == profileKey, ct);
                if (existing != null) return existing;
            }

            var conversation = new SQL.ChatConversation { UserId = userId, Profile = profileKey };
            db.ChatConversations.Add(conversation);
            await db.SaveChangesAsync(ct);
            return conversation;
        }

        private static string TitleFrom(string message) =>
            message.Length > 30 ? string.Concat(message.AsSpan(0, 30), "…") : message;
    }
}
