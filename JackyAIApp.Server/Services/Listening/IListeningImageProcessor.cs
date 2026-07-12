namespace JackyAIApp.Server.Services.Listening
{
    /// <summary>
    /// Normalizes a raw image (from Gemini-batch import or AI generation) down to a consistent,
    /// web-friendly JPEG before it's uploaded to Blob Storage — shared by the bulk importer tool
    /// and the admin AI-generation endpoint so both produce identically-sized assets.
    /// </summary>
    public interface IListeningImageProcessor
    {
        byte[] ResizeAndCompress(byte[] imageBytes, int maxDimension = 1024, int targetMaxBytes = 300 * 1024);
    }
}
