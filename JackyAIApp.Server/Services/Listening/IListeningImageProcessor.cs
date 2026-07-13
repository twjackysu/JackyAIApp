namespace JackyAIApp.Server.Services.Listening
{
    /// <summary>
    /// Normalizes a raw image (from Gemini-batch import or AI generation) down to a consistent,
    /// web-friendly JPEG before it's uploaded to Blob Storage — shared by the bulk importer tool
    /// and the admin AI-generation endpoint so both produce identically-sized assets. Center-crops
    /// to the target aspect ratio so every question image is uniform regardless of source aspect.
    /// </summary>
    public interface IListeningImageProcessor
    {
        byte[] ResizeAndCompress(byte[] imageBytes, int targetWidth = 800, int targetHeight = 600, int targetMaxBytes = 300 * 1024);
    }
}
