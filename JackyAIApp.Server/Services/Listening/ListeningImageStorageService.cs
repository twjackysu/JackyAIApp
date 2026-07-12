using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace JackyAIApp.Server.Services.Listening
{
    public class ListeningImageStorageService(BlobServiceClient blobServiceClient) : IListeningImageStorageService
    {
        private const string ContainerName = "listening-images";

        public async Task<string> UploadAsync(string blobName, byte[] imageBytes, CancellationToken ct = default)
        {
            var container = blobServiceClient.GetBlobContainerClient(ContainerName);
            await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);

            var blobClient = container.GetBlobClient(blobName);
            using var stream = new MemoryStream(imageBytes);
            await blobClient.UploadAsync(
                stream,
                new BlobHttpHeaders { ContentType = "image/jpeg" },
                cancellationToken: ct);

            return blobClient.Uri.ToString();
        }
    }
}
