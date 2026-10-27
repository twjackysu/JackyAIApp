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

        /// <summary>
        /// The largest recording transcribed, in bytes. A transcription costs one unit of the budget
        /// whatever its length, while Whisper bills by the minute and takes up to 25 MB, which at a low
        /// bitrate is hours of audio (D-159). The exam pages stop a recording at two minutes and record at
        /// 64 kbps (the client's <c>MAX_RECORDING_SECONDS</c>, <c>RECORDING_BITS_PER_SECOND</c>), about 1 MB,
        /// so 4 MB leaves room for a browser that ignores the bitrate it was asked for.
        /// </summary>
        public const int MaxAudioBytes = 4 * 1024 * 1024;

        /// <summary>Transcribes audio to text using OpenAI Whisper.</summary>
        [HttpPost("whisper/transcribe")]
        [UsageLimit(UsageFeature.AudioTranscription)]
        [RequestSizeLimit(MaxAudioBytes + 64 * 1024)]
        public async Task<IActionResult> TranscribeAudio(IFormFile audioFile)
        {
            if (audioFile == null || audioFile.Length == 0)
            {
                return responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "No audio file provided.");
            }

            // The body limit above also counts the form's own bytes; the recording itself is held to
            // MaxAudioBytes. Refused before Whisper is asked, so the charge is refunded.
            if (audioFile.Length > MaxAudioBytes)
            {
                return responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "The recording is too long.");
            }

            using var stream = audioFile.OpenReadStream();
            return responseFactory.ToActionResult(await conversations.TranscribeAudioAsync(stream, audioFile.FileName));
        }
    }
}
