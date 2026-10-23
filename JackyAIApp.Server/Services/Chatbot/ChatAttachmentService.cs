using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Chatbot
{
    public static class ChatAttachmentLimits
    {
        /// <summary>Files sent with one message, and unsent uploads kept per user.</summary>
        public const int MaxPerMessage = 5;

        /// <summary>An upload never sent is deleted once it is this old.</summary>
        public static readonly TimeSpan UnsentLifetime = TimeSpan.FromHours(24);

        /// <summary>Largest upload of any kind.</summary>
        public const long MaxUploadBytes = AttachmentTextExtractor.MaxBytes > AttachmentFileProcessor.MaxBytes
            ? AttachmentTextExtractor.MaxBytes
            : AttachmentFileProcessor.MaxBytes;
    }

    /// <summary>The stored image or PDF of an attachment.</summary>
    public sealed record ChatAttachmentContent(byte[] Bytes, string MediaType);

    /// <summary>
    /// The signed-in user's chat attachments: uploading one to send with the next message, removing
    /// one not sent yet, reading back its stored file, and picking out the ones a message sends.
    /// Someone else's attachment answers NotFound (or is left out). An attachment's row is in SQL;
    /// an image's or PDF's file is also in <see cref="IChatAttachmentStore"/> and Anthropic's Files
    /// API, deleted from both once the row is gone.
    /// </summary>
    public interface IChatAttachmentService
    {
        /// <summary>
        /// Readies a file to be sent with the next chat message: a Word, Excel, CSV or text file is
        /// read into text and only the text is kept; an image (scaled down to what the model reads)
        /// or a PDF is kept as a file.
        /// </summary>
        Task<ServiceResult<ChatAttachmentDto>> UploadAsync(string fileName, long length, Stream content, CancellationToken ct);

        /// <summary>Removes one of the user's uploads that has not been sent yet.</summary>
        Task<ServiceResult<NoData>> DeleteUnsentAsync(long attachmentId, CancellationToken ct);

        /// <summary>The stored image or PDF of one of the user's attachments, sent or not.</summary>
        Task<ServiceResult<ChatAttachmentContent>> GetContentAsync(long attachmentId, CancellationToken ct);

        /// <summary>
        /// The user's own uploads among these ids that have not been sent yet, at most
        /// <see cref="ChatAttachmentLimits.MaxPerMessage"/>; an id that is someone else's, already
        /// sent, or deleted is left out rather than failing the message.
        /// </summary>
        Task<List<ChatAttachment>> TakeUnsentAsync(IEnumerable<long>? attachmentIds, CancellationToken ct);

        /// <summary>The user's attachments sent with this message.</summary>
        Task<List<ChatAttachment>> SentWithAsync(long messageId, CancellationToken ct);

        /// <summary>Deletes attachments' files from both stores, once their rows are gone.</summary>
        Task DeleteStoredFilesAsync(IReadOnlyCollection<StoredFile> files, CancellationToken ct);
    }

    public class ChatAttachmentService(
        ILogger<ChatAttachmentService> logger,
        AzureSQLDBContext dbContext,
        IChatAttachmentStore attachmentStore,
        IAnthropicFileStore anthropicFiles,
        ICurrentUser currentUser) : IChatAttachmentService
    {
        private static ServiceFailure AttachmentNotFound => ServiceResult.Fail(ErrorCodes.NotFound, "Attachment not found.");

        public async Task<ServiceResult<ChatAttachmentDto>> UploadAsync(string fileName, long length, Stream content, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            fileName = Path.GetFileName(fileName);
            var kind = AttachmentTextExtractor.KindOf(fileName);
            if (kind == null)
                return ServiceResult.Fail(ErrorCodes.AttachmentUnsupportedType, "Unsupported file type.");
            if (length > ChatAttachmentLimits.MaxUploadBytes)
                return ServiceResult.Fail(ErrorCodes.PayloadTooLarge, "File is too large.");

            var attachment = new ChatAttachment
            {
                UserId = userId,
                FileName = fileName.Length > 260 ? fileName[^260..] : fileName,
                Kind = kind,
                Text = "",
            };
            PreparedAttachmentFile? prepared = null;
            try
            {
                if (kind == AttachmentKinds.Image)
                    prepared = AttachmentFileProcessor.PrepareImage(content);
                else if (kind == AttachmentKinds.Pdf)
                    prepared = AttachmentFileProcessor.PreparePdf(content);
                else
                {
                    var extracted = AttachmentTextExtractor.Extract(kind, content);
                    if (string.IsNullOrWhiteSpace(extracted.Text))
                        return ServiceResult.Fail(ErrorCodes.AttachmentUnreadable, "The file has no text.");
                    attachment.Text = extracted.Text;
                    attachment.Truncated = extracted.Truncated;
                }
            }
            catch (UnreadableAttachmentException ex)
            {
                logger.LogWarning(ex, "User {UserId} uploaded an unreadable {Kind} attachment", userId, kind);
                return ServiceResult.Fail(ErrorCodes.AttachmentUnreadable, "The file could not be read.");
            }
            catch (ImageTooLargeException ex)
            {
                // A page loaded before images were downsized in the browser: a reload sends a smaller one.
                logger.LogInformation(ex, "User {UserId} uploaded an image larger than the browser makes one", userId);
                return ServiceResult.Fail(ErrorCodes.AttachmentImageTooLarge, "The image is larger than expected; reload the page.");
            }
            catch (TooManyPagesException ex)
            {
                logger.LogInformation("User {UserId} uploaded a PDF of {Pages} pages", userId, ex.Pages);
                return ServiceResult.Fail(ErrorCodes.AttachmentTooManyPages, "The PDF has too many pages.");
            }

            await DeleteUnsentAttachmentsAsync(userId, ct);

            if (prepared != null)
            {
                attachment.BlobName = Guid.NewGuid().ToString("N");
                attachment.MediaType = prepared.MediaType;
                attachment.PageCount = prepared.PageCount;
                await attachmentStore.SaveAsync(attachment.BlobName, prepared.Bytes, prepared.MediaType, ct);
                // Uploaded once here, then only referred to on each turn. If this fails, the first
                // message that sends the file tries again (AnthropicChatService).
                attachment.AnthropicFileId = await anthropicFiles.UploadAsync(prepared.Bytes, prepared.MediaType, ct);
            }
            dbContext.ChatAttachments.Add(attachment);
            await dbContext.SaveChangesAsync(ct);

            return ServiceResult.Ok(ToDto(attachment));
        }

        /// <summary>
        /// Uploads that were never sent (a message that failed, a tab that was closed) are not kept:
        /// anything past a day old, and past the newest few, makes room for the one being uploaded.
        /// The client never holds more than one message's worth of unsent files.
        /// </summary>
        private async Task DeleteUnsentAttachmentsAsync(string userId, CancellationToken ct)
        {
            var staleBefore = DateTime.UtcNow - ChatAttachmentLimits.UnsentLifetime;
            var keep = await dbContext.ChatAttachments
                .Where(a => a.UserId == userId && a.MessageId == null && a.CreatedAt >= staleBefore)
                .OrderByDescending(a => a.Id)
                .Select(a => a.Id)
                .Take(ChatAttachmentLimits.MaxPerMessage - 1)
                .ToListAsync(ct);
            var stale = dbContext.ChatAttachments
                .Where(a => a.UserId == userId && a.MessageId == null && !keep.Contains(a.Id));
            var files = await StoredFiles(stale).ToListAsync(ct);
            await stale.ExecuteDeleteAsync(ct);
            await DeleteStoredFilesAsync(files, ct);
        }

        public async Task<ServiceResult<NoData>> DeleteUnsentAsync(long attachmentId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var attachment = await dbContext.ChatAttachments
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.UserId == userId && a.MessageId == null, ct);
            if (attachment == null)
                return AttachmentNotFound;

            dbContext.ChatAttachments.Remove(attachment);
            await dbContext.SaveChangesAsync(ct);
            await DeleteStoredFilesAsync([new StoredFile(attachment.BlobName, attachment.AnthropicFileId)], ct);

            return ServiceResult.Ok();
        }

        public async Task<ServiceResult<ChatAttachmentContent>> GetContentAsync(long attachmentId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var attachment = await dbContext.ChatAttachments
                .Where(a => a.Id == attachmentId && a.UserId == userId && a.BlobName != null)
                .Select(a => new { a.BlobName, a.MediaType })
                .FirstOrDefaultAsync(ct);
            var content = attachment == null ? null : await attachmentStore.ReadAsync(attachment.BlobName!, ct);
            if (content == null)
                return AttachmentNotFound;

            return ServiceResult.Ok(new ChatAttachmentContent(content, attachment!.MediaType ?? "application/octet-stream"));
        }

        public async Task<List<ChatAttachment>> TakeUnsentAsync(IEnumerable<long>? attachmentIds, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var ids = (attachmentIds ?? []).Distinct().Take(ChatAttachmentLimits.MaxPerMessage).ToList();
            if (ids.Count == 0)
                return [];

            return await dbContext.ChatAttachments
                .Where(a => ids.Contains(a.Id) && a.UserId == userId && a.MessageId == null)
                .OrderBy(a => a.Id)
                .ToListAsync(ct);
        }

        public async Task<List<ChatAttachment>> SentWithAsync(long messageId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            return await dbContext.ChatAttachments
                .Where(a => a.MessageId == messageId && a.UserId == userId)
                .ToListAsync(ct);
        }

        /// <summary>Where the files of these attachments are kept, for those that have any.</summary>
        internal static IQueryable<StoredFile> StoredFiles(IQueryable<ChatAttachment> attachments) =>
            attachments
                .Where(a => a.BlobName != null || a.AnthropicFileId != null)
                .Select(a => new StoredFile(a.BlobName, a.AnthropicFileId));

        public async Task DeleteStoredFilesAsync(IReadOnlyCollection<StoredFile> files, CancellationToken ct)
        {
            var blobs = files.Where(f => f.BlobName != null).Select(f => f.BlobName!).ToList();
            var fileIds = files.Where(f => f.AnthropicFileId != null).Select(f => f.AnthropicFileId!).ToList();
            if (blobs.Count > 0) await attachmentStore.DeleteAsync(blobs, ct);
            if (fileIds.Count > 0) await anthropicFiles.DeleteAsync(fileIds, ct);
        }

        private static ChatAttachmentDto ToDto(ChatAttachment a) => new()
        {
            Id = a.Id,
            FileName = a.FileName,
            Kind = a.Kind,
            CharCount = a.Text.Length,
            Truncated = a.Truncated,
            PageCount = a.PageCount,
        };
    }
}
