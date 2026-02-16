using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using JackyAIApp.Server.DTO;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace JackyAIApp.Server.Services.OpenAI
{
    public class OpenAIPromptService : IOpenAIPromptService
    {
        private readonly IOpenAIService _openAIService;
        private readonly ILogger<OpenAIPromptService> _logger;

        public OpenAIPromptService(IOpenAIService openAIService, ILogger<OpenAIPromptService> logger)
        {
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<(T? Result, string? Error)> GetCompletionAsync<T>(
            string systemPromptPath,
            string userMessage,
            string? model = null,
            IEnumerable<(string User, string Assistant)>? examples = null) where T : class
        {
            if (!File.Exists(systemPromptPath))
            {
                _logger.LogError("System prompt file not found: {Path}", systemPromptPath);
                return (null, $"System prompt file not found: {systemPromptPath}");
            }

            var systemPrompt = await File.ReadAllTextAsync(systemPromptPath);
            return await GetCompletionWithSystemPromptAsync<T>(systemPrompt, userMessage, model, examples);
        }

        public async Task<(T? Result, string? Error)> GetCompletionWithSystemPromptAsync<T>(
            string systemPrompt,
            string userMessage,
            string? model = null,
            IEnumerable<(string User, string Assistant)>? examples = null) where T : class
        {
            var messages = new List<ChatMessage> { ChatMessage.FromSystem(systemPrompt) };

            // Add few-shot examples if provided
            if (examples != null)
            {
                foreach (var (user, assistant) in examples)
                {
                    messages.Add(ChatMessage.FromUser(user));
                    messages.Add(ChatMessage.FromAssistant(assistant));
                }
            }

            messages.Add(ChatMessage.FromUser(userMessage));

            var messagesTuples = messages.Select(m => (m.Role ?? "user", m.Content ?? "")).ToList();
            return await GetCompletionWithMessagesAsync<T>(messagesTuples, model);
        }

        public async Task<(T? Result, string? Error)> GetCompletionWithMessagesAsync<T>(
            IEnumerable<(string Role, string Content)> messages,
            string? model = null) where T : class
        {
            try
            {
                var chatMessages = messages.Select(m => m.Role.ToLower() switch
                {
                    "system" => ChatMessage.FromSystem(m.Content),
                    "assistant" => ChatMessage.FromAssistant(m.Content),
                    _ => ChatMessage.FromUser(m.Content)
                }).ToList();

                var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
                {
                    Messages = chatMessages,
                    Model = model ?? NewModels.GPT_5_NANO
                });

                if (!completionResult.Successful)
                {
                    var errorMsg = completionResult.Error?.Message ?? "OpenAI request failed";
                    _logger.LogError("OpenAI completion failed: {Error}", errorMsg);
                    return (null, errorMsg);
                }

                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return (null, "OpenAI returned empty response");
                }

                // Clean JSON from markdown code blocks if present
                var cleanedContent = CleanJsonResponse(content);

                var result = JsonConvert.DeserializeObject<T>(cleanedContent);
                if (result == null)
                {
                    _logger.LogWarning("Failed to deserialize OpenAI response: {Content}", content);
                    return (null, "Failed to parse OpenAI response");
                }

                return (result, null);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization failed");
                return (null, "Failed to parse OpenAI response as JSON");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during OpenAI completion");
                return (null, "Unexpected error during OpenAI request");
            }
        }

        public async Task<(string? Transcription, string? Error)> TranscribeAudioAsync(Stream audioStream, string fileName)
        {
            try
            {
                var audioBytes = new byte[audioStream.Length];
                await audioStream.ReadAsync(audioBytes, 0, (int)audioStream.Length);

                var response = await _openAIService.Audio.CreateTranscription(new AudioCreateTranscriptionRequest
                {
                    File = audioBytes,
                    FileName = fileName,
                    Model = Models.WhisperV1,
                    ResponseFormat = "json"
                });

                if (!response.Successful)
                {
                    var errorMsg = response.Error?.Message ?? "Whisper transcription failed";
                    _logger.LogError("Whisper transcription failed: {Error}", errorMsg);
                    return (null, errorMsg);
                }

                return (response.Text, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during audio transcription");
                return (null, "Error processing audio file");
            }
        }

        /// <summary>
        /// Removes markdown code block markers from JSON response
        /// </summary>
        private static string CleanJsonResponse(string content)
        {
            string pattern = @"^\s*```\s*json\s*|^\s*```\s*|```\s*$";
            string result = Regex.Replace(content, pattern, "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            return result.Trim();
        }
    }
}
