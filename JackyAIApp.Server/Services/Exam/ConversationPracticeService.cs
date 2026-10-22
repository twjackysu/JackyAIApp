using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// Scenario-based conversation practice: the AI's opening, its replies with grammar
    /// corrections, the end-of-session recap, and transcribing what the user said.
    /// </summary>
    public interface IConversationPracticeService
    {
        Task<ServiceResult<ConversationStartResponse>> StartAsync(ConversationStartRequest request);

        Task<ServiceResult<ConversationResponseResponse>> RespondAsync(ConversationResponseRequest request);

        /// <summary>Produces an end-of-session recap (corrections, better phrasings, word suggestions).</summary>
        Task<ServiceResult<ConversationSummaryResponse>> SummarizeAsync(ConversationSummaryRequest request);

        /// <summary>The text of a recording (Whisper), with feedback on its delivery when it is long enough.</summary>
        Task<ServiceResult<WhisperTranscriptionResponse>> TranscribeAudioAsync(Stream audioStream, string fileName);
    }

    public class ConversationPracticeService(
        ILogger<ConversationPracticeService> logger,
        IOpenAIPromptService promptService,
        IUserAccountService accounts) : IConversationPracticeService
    {
        private readonly ILogger<ConversationPracticeService> _logger = logger;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly IUserAccountService _accounts = accounts;

        public async Task<ServiceResult<ConversationStartResponse>> StartAsync(ConversationStartRequest request)
        {
            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            if (request == null || string.IsNullOrEmpty(request.Scenario) ||
                string.IsNullOrEmpty(request.UserRole) || string.IsNullOrEmpty(request.AiRole))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Scenario, UserRole, and AiRole are required.");
            }

            // Was previously a raw ChatCompletion call outside IOpenAIPromptService — bypassed
            // the JSON-force/token-cap reliability fixes and, more importantly, the AiUsageLog
            // instrumentation, so this endpoint's cost was completely invisible. Routed through
            // the shared wrapper now. It used gpt-4o-mini at temperature 0.8 for scenario variety;
            // gpt-6-luna is a reasoning model that takes only the default temperature (1), which
            // varies at least as much.
            var (conversationStart, error) = await _promptService.GetCompletionAsync<ConversationStartResponse>(
                "Prompt/Exam/ConversationStartSystem.txt",
                AiJson.Serialize(request),
                model: NewModels.GPT_6_LUNA,
                feature: "exam.conversation_start");

            if (conversationStart == null)
            {
                _logger.LogError("Failed to generate conversation start: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the conversation scenario.");
            }

            _logger.LogInformation("Generated conversation start for scenario: {Scenario}", request.Scenario);
            return ServiceResult.Ok(conversationStart);
        }

        public async Task<ServiceResult<ConversationResponseResponse>> RespondAsync(ConversationResponseRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.UserMessage) || request.ConversationContext == null)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Invalid conversation request.");
            }

            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            var (response, error) = await _promptService.GetCompletionAsync<ConversationResponseResponse>(
                "Prompt/Exam/ConversationResponseSystem.txt",
                AiJson.Serialize(request));

            if (response == null)
            {
                _logger.LogError("Failed to generate conversation response: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the conversation response.");
            }

            _logger.LogInformation("Generated conversation response for turn {Turn}", request.ConversationContext.TurnNumber);
            return ServiceResult.Ok(response);
        }

        public async Task<ServiceResult<ConversationSummaryResponse>> SummarizeAsync(ConversationSummaryRequest request)
        {
            if (request == null || request.ConversationContext == null ||
                request.ConversationHistory == null || request.ConversationHistory.Count == 0)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Invalid conversation summary request.");
            }

            var (summary, error) = await _promptService.GetCompletionAsync<ConversationSummaryResponse>(
                "Prompt/Exam/ConversationSummarySystem.txt",
                AiJson.Serialize(request));

            if (summary == null)
            {
                _logger.LogError("Failed to generate conversation summary: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the conversation summary.");
            }

            _logger.LogInformation("Generated conversation summary for scenario: {Scenario}", request.ConversationContext.Scenario);
            return ServiceResult.Ok(summary);
        }

        public async Task<ServiceResult<WhisperTranscriptionResponse>> TranscribeAudioAsync(Stream audioStream, string fileName)
        {
            if (!await _accounts.ExistsAsync())
            {
                return UserAccountFailures.Missing;
            }

            var (result, error) = await _promptService.TranscribeAudioAsync(audioStream, fileName);

            if (result == null)
            {
                _logger.LogError("Whisper transcription failed: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, "Failed to transcribe audio.");
            }

            var text = result.Text.Trim();
            var deliveryFeedback = SpeechFluencyAnalyzer.Analyze(text, result.DurationSeconds, result.Words);

            _logger.LogInformation("Whisper transcription successful");
            return ServiceResult.Ok(new WhisperTranscriptionResponse { Text = text, DeliveryFeedback = deliveryFeedback });
        }
    }
}
