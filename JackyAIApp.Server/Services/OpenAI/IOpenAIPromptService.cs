namespace JackyAIApp.Server.Services.OpenAI
{
    /// <summary>
    /// Service for handling OpenAI chat completions with prompt files
    /// </summary>
    public interface IOpenAIPromptService
    {
        /// <summary>
        /// Sends a chat completion request with a system prompt from file and returns the parsed result
        /// </summary>
        /// <typeparam name="T">The type to deserialize the response to</typeparam>
        /// <param name="systemPromptPath">Path to the system prompt file</param>
        /// <param name="userMessage">The user message to send</param>
        /// <param name="model">The model to use (defaults to GPT_5_NANO)</param>
        /// <param name="examples">Optional list of (user, assistant) example pairs for few-shot learning</param>
        /// <returns>The deserialized response or null if failed, along with any error message</returns>
        Task<(T? Result, string? Error)> GetCompletionAsync<T>(
            string systemPromptPath,
            string userMessage,
            string? model = null,
            IEnumerable<(string User, string Assistant)>? examples = null) where T : class;

        /// <summary>
        /// Sends a chat completion request with a system prompt string and returns the parsed result
        /// </summary>
        Task<(T? Result, string? Error)> GetCompletionWithSystemPromptAsync<T>(
            string systemPrompt,
            string userMessage,
            string? model = null,
            IEnumerable<(string User, string Assistant)>? examples = null) where T : class;

        /// <summary>
        /// Sends a chat completion request with custom messages
        /// </summary>
        Task<(T? Result, string? Error)> GetCompletionWithMessagesAsync<T>(
            IEnumerable<(string Role, string Content)> messages,
            string? model = null) where T : class;

        /// <summary>
        /// Transcribes audio using Whisper
        /// </summary>
        Task<(string? Transcription, string? Error)> TranscribeAudioAsync(Stream audioStream, string fileName);
    }
}
