using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>Where the files behind image and PDF chat attachments are kept (their rows are in SQL).</summary>
    public interface IChatAttachmentStore
    {
        Task SaveAsync(string name, byte[] content, string mediaType, CancellationToken ct);

        /// <summary>The file, or null when it is gone.</summary>
        Task<byte[]?> ReadAsync(string name, CancellationToken ct);

        /// <summary>Deletes what exists of these; a failure is logged, never thrown — a leftover file
        /// costs storage, while failing the caller would keep a row the user asked to delete.</summary>
        Task DeleteAsync(IEnumerable<string> names, CancellationToken ct);
    }

    /// <summary>A private blob container (never public: the files are only served through the API,
    /// to the user who uploaded them).</summary>
    public class BlobChatAttachmentStore(BlobServiceClient blobServiceClient, ILogger<BlobChatAttachmentStore> logger)
        : IChatAttachmentStore
    {
        private const string ContainerName = "chat-attachments";
        private int _containerReady;

        private async Task<BlobContainerClient> ContainerAsync(CancellationToken ct)
        {
            var container = blobServiceClient.GetBlobContainerClient(ContainerName);
            if (Interlocked.CompareExchange(ref _containerReady, 1, 0) == 0)
                await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
            return container;
        }

        public async Task SaveAsync(string name, byte[] content, string mediaType, CancellationToken ct)
        {
            var container = await ContainerAsync(ct);
            using var stream = new MemoryStream(content);
            await container.GetBlobClient(name).UploadAsync(
                stream, new BlobHttpHeaders { ContentType = mediaType }, cancellationToken: ct);
        }

        public async Task<byte[]?> ReadAsync(string name, CancellationToken ct)
        {
            try
            {
                var container = await ContainerAsync(ct);
                var result = await container.GetBlobClient(name).DownloadContentAsync(ct);
                return result.Value.Content.ToArray();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task DeleteAsync(IEnumerable<string> names, CancellationToken ct)
        {
            var container = await ContainerAsync(ct);
            foreach (var name in names)
            {
                try
                {
                    await container.GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not delete chat attachment blob {Blob}", name);
                }
            }
        }
    }

    /// <summary>
    /// Used only where no Blob Storage is configured (local development): files live in memory
    /// and are gone when the app restarts.
    /// </summary>
    public class InMemoryChatAttachmentStore : IChatAttachmentStore
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new();

        public Task SaveAsync(string name, byte[] content, string mediaType, CancellationToken ct)
        {
            _files[name] = content;
            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadAsync(string name, CancellationToken ct) =>
            Task.FromResult(_files.TryGetValue(name, out var content) ? content : null);

        public Task DeleteAsync(IEnumerable<string> names, CancellationToken ct)
        {
            foreach (var name in names) _files.TryRemove(name, out _);
            return Task.CompletedTask;
        }
    }
}
