using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Prompt;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using ResponseFormatType = Betalgo.Ranul.OpenAI.Contracts.Enums.ResponseFormat;
using ReasoningEffortType = Betalgo.Ranul.OpenAI.Contracts.Enums.ReasoningEffort;

namespace JackyAIApp.Server.Services.OpenAI
{
    public class OpenAIPromptService : IOpenAIPromptService
    {
        private readonly IOpenAIService _openAIService;
        private readonly IPromptLoader _promptLoader;
        private readonly ILogger<OpenAIPromptService> _logger;

        public OpenAIPromptService(
            IOpenAIService openAIService,
            IPromptLoader promptLoader,
            ILogger<OpenAIPromptService> logger)
        {
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _promptLoader = promptLoader ?? throw new ArgumentNullException(nameof(promptLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<(T? Result, string? Error)> GetCompletionAsync<T>(
            string systemPromptPath,
            string userMessage,
            string? model = null,
            IEnumerable<(string User, string Assistant)>? examples = null) where T : class
        {
            var systemPrompt = await _promptLoader.GetPromptAsync(systemPromptPath);
            if (systemPrompt == null)
            {
                _logger.LogError("System prompt not found: {Path}", systemPromptPath);
                return (null, $"System prompt not found: {systemPromptPath}");
            }

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

            var messagesTuples = messages.Select(m => (m.Role?.ToString().ToLower() ?? "user", m.Content ?? "")).ToList();
            return await GetCompletionWithMessagesAsync<T>(messagesTuples, model);
        }

        // "gpt-5*" is this app's only reasoning-family model; reasoning_effort is rejected by the
        // OpenAI API on non-reasoning models (e.g. gpt-4.1-nano callers that pass an explicit
        // model), so it must only be sent when the resolved model actually supports it.
        private static bool IsReasoningModel(string model) =>
            model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);

        // Every prompt file routed through here must mention the word "json" somewhere in its
        // system/user text — the API rejects response_format=json_object otherwise.
        private const int DefaultMaxCompletionTokens = 4096;

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

                var resolvedModel = model ?? NewModels.GPT_5_NANO;
                var request = new ChatCompletionCreateRequest
                {
                    Messages = chatMessages,
                    Model = resolvedModel,
                    // Forces valid JSON instead of relying on the model to follow the prompt's
                    // format instructions — occasional prose/markdown-wrapped/truncated replies
                    // were the main cause of "OpenAI could not generate ..." failures.
                    ResponseFormat = new ResponseFormat { Type = ResponseFormatType.JsonObject },
                    // Without a cap, a reasoning model can spend its whole budget on invisible
                    // reasoning tokens and return empty content for a large structured response
                    // (e.g. a multi-sense dictionary entry). 4096 comfortably covers every prompt
                    // in this app.
                    MaxCompletionTokens = DefaultMaxCompletionTokens,
                };
                if (IsReasoningModel(resolvedModel))
                {
                    // These are extraction/formatting tasks, not tasks needing deep reasoning —
                    // low effort is faster and cheaper with no meaningful quality loss.
                    request.ReasoningEffort = ReasoningEffortType.Low;
                }

                var completionResult = await _openAIService.ChatCompletion.CreateCompletion(request);

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
                await audioStream.ReadExactlyAsync(audioBytes);

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
