namespace JackyAIApp.Server.Services.Listening
{
    /// <summary>Uploads a processed listening-question image to Blob Storage and returns its URL.</summary>
    public interface IListeningImageStorageService
    {
        Task<string> UploadAsync(string blobName, byte[] imageBytes, CancellationToken ct = default);
    }
}
