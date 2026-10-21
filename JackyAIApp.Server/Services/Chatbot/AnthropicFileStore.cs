using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Files;
using JackyAIApp.Server.Configuration;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>
    /// Copies of image and PDF attachments in Anthropic's Files API, so a turn refers to each by
    /// its file_id instead of sending the whole file again: the request stays small however many
    /// files the conversation holds (the API takes at most 32 MB per request). The model reads a
    /// referenced file as it would the bytes, at the same input-token price.
    /// <para>
    /// The copy in <see cref="IChatAttachmentStore"/> stays the original: a file uploaded to
    /// Anthropic cannot be downloaded again, so thumbnails and opening a PDF are served from there,
    /// and a file_id that no longer resolves is replaced by uploading that copy again.
    /// </para>
    /// <para>
    /// Uploaded files belong to the API key's whole workspace, not to a user: which file is whose
    /// is kept on the ChatAttachment row, and a file_id never comes from the client.
    /// </para>
    /// </summary>
    public interface IAnthropicFileStore
    {
        /// <summary>The new file's id, or null when it could not be uploaded (no key, the API
        /// failing) — the caller then sends the file's bytes instead.</summary>
        Task<string?> UploadAsync(byte[] content, string mediaType, CancellationToken ct);

        /// <summary>Deletes what exists of these; a failure is logged, never thrown.</summary>
        Task DeleteAsync(IEnumerable<string> fileIds, CancellationToken ct);
    }

    public class AnthropicFileStore(IOptionsMonitor<Settings> settings, ILogger<AnthropicFileStore> logger)
        : IAnthropicFileStore
    {
        private AnthropicClient? Client()
        {
            var apiKey = settings.CurrentValue.AnthropicApiKey;
            return string.IsNullOrEmpty(apiKey) ? null : new AnthropicClient { ApiKey = apiKey };
        }

        public async Task<string?> UploadAsync(byte[] content, string mediaType, CancellationToken ct)
        {
            var client = Client();
            if (client == null) return null;
            try
            {
                // A name of our own: the user's file name stays in our database, not in Anthropic's.
                var extension = mediaType switch
                {
                    "application/pdf" => ".pdf",
                    "image/png" => ".png",
                    _ => ".jpg",
                };
                using var stream = new MemoryStream(content);
                var uploaded = await client.Files.Upload(new FileUploadParams
                {
                    File = new BinaryContent
                    {
                        Stream = stream,
                        FileName = $"{Guid.NewGuid():N}{extension}",
                        ContentType = new(mediaType),
                    },
                }, ct);
                return uploaded.ID;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not upload a chat attachment to the Anthropic Files API");
                return null;
            }
        }

        public async Task DeleteAsync(IEnumerable<string> fileIds, CancellationToken ct)
        {
            var client = Client();
            if (client == null) return;
            foreach (var fileId in fileIds)
            {
                try
                {
                    await client.Files.Delete(fileId, cancellationToken: ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Could not delete Anthropic file {FileId}", fileId);
                }
            }
        }
    }
}
