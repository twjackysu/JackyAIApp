using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>Conversation practice and the transcription of what the user said, under api/Exam.</summary>
    [Authorize]
    [Route("api/Exam")]
    [ApiController]
    public class ConversationPracticeController(
        IMyResponseFactory responseFactory,
        IConversationPracticeService conversations) : ControllerBase
    {
        /// <summary>Starts a new conversation test by generating a scenario and first message.</summary>
        [HttpPost("conversation/start")]
        [UsageLimit(UsageFeature.ConversationStart)]
        public async Task<IActionResult> StartConversationTest([FromBody] ConversationStartRequest request) =>
            responseFactory.ToActionResult(await conversations.StartAsync(request));

        /// <summary>Processes user's response in a conversation and generates AI response with corrections if needed.</summary>
        [HttpPost("conversation/respond")]
        [UsageLimit(UsageFeature.ConversationResponse)]
        public async Task<IActionResult> RespondToConversation([FromBody] ConversationResponseRequest request) =>
            responseFactory.ToActionResult(await conversations.RespondAsync(request));

        /// <summary>Generates an end-of-session recap for a conversation (corrections, phrasings, word suggestions).</summary>
        [HttpPost("conversation/summary")]
        [UsageLimit(UsageFeature.ConversationSummary)]
        public async Task<IActionResult> SummarizeConversation([FromBody] ConversationSummaryRequest request) =>
            responseFactory.ToActionResult(await conversations.SummarizeAsync(request));

        /// <summary>Transcribes audio to text using OpenAI Whisper.</summary>
        [HttpPost("whisper/transcribe")]
        [UsageLimit(UsageFeature.AudioTranscription)]
        public async Task<IActionResult> TranscribeAudio(IFormFile audioFile)
        {
            if (audioFile == null || audioFile.Length == 0)
            {
                return responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "No audio file provided.");
            }

            using var stream = audioFile.OpenReadStream();
            return responseFactory.ToActionResult(await conversations.TranscribeAudioAsync(stream, audioFile.FileName));
        }
    }
}
