namespace JackyAIApp.Server.Services.Prompt
{
    /// <summary>
    /// Service for loading prompt templates from embedded resources
    /// </summary>
    public interface IPromptLoader
    {
        /// <summary>
        /// Load a prompt template by its resource path
        /// </summary>
        /// <param name="promptPath">Path like "Prompt/Exam/ClozeSystem.txt"</param>
        /// <returns>The prompt content, or null if not found</returns>
        string? GetPrompt(string promptPath);

        /// <summary>
        /// Load a prompt template asynchronously
        /// </summary>
        Task<string?> GetPromptAsync(string promptPath);

        /// <summary>
        /// Check if a prompt exists
        /// </summary>
        bool PromptExists(string promptPath);
    }
}
