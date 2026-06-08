using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ExamController(
        IMyResponseFactory responseFactory,
        IExamService examService) : ControllerBase
    {
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IExamService _examService = examService;

        /// <summary>Generates a cloze test for the user based on their unfamiliar words.</summary>
        [HttpGet("cloze")]
        [ConsumeCredits(CreditCosts.ClozeTest, "cloze_test", "Cloze test generation")]
        public async Task<IActionResult> GetClozeTest() =>
            ToActionResult(await _examService.GenerateWordExamAsync(ExamType.Cloze));

        /// <summary>Generates a translation test for the user based on their unfamiliar words.</summary>
        [HttpGet("translation")]
        [ConsumeCredits(CreditCosts.TranslationTest, "translation_test", "Translation test generation")]
        public async Task<IActionResult> GetTranslationTest() =>
            ToActionResult(await _examService.GenerateWordExamAsync(ExamType.Translation));

        /// <summary>Generates a translation quality grading.</summary>
        [HttpPost("translation/quality_grading")]
        public async Task<IActionResult> GetTranslationQualityGrading([FromBody] TranslationTestUserResponse userResponse) =>
            ToActionResult(await _examService.GradeTranslationAsync(userResponse));

        /// <summary>Starts a new conversation test by generating a scenario and first message.</summary>
        [HttpPost("conversation/start")]
        [ConsumeCredits(CreditCosts.ConversationStart, "conversation_start", "Conversation test started")]
        public async Task<IActionResult> StartConversationTest([FromBody] ConversationStartRequest request) =>
            ToActionResult(await _examService.StartConversationAsync(request));

        /// <summary>Processes user's response in a conversation and generates AI response with corrections if needed.</summary>
        [HttpPost("conversation/respond")]
        public async Task<IActionResult> RespondToConversation([FromBody] ConversationResponseRequest request) =>
            ToActionResult(await _examService.RespondToConversationAsync(request));

        /// <summary>Transcribes audio to text using OpenAI Whisper.</summary>
        [HttpPost("whisper/transcribe")]
        public async Task<IActionResult> TranscribeAudio(IFormFile audioFile)
        {
            if (audioFile == null || audioFile.Length == 0)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "No audio file provided.");
            }

            using var stream = audioFile.OpenReadStream();
            return ToActionResult(await _examService.TranscribeAudioAsync(stream, audioFile.FileName));
        }

        /// <summary>Generates a sentence formation test for the user based on their unfamiliar words.</summary>
        [HttpGet("sentence")]
        [ConsumeCredits(CreditCosts.SentenceTest, "sentence_test", "Sentence test generation")]
        public async Task<IActionResult> GetSentenceTest() =>
            ToActionResult(await _examService.GenerateWordExamAsync(ExamType.Sentence));

        /// <summary>Evaluates a user's sentence formation response and provides detailed feedback.</summary>
        [HttpPost("sentence/evaluate")]
        public async Task<IActionResult> EvaluateSentence([FromBody] SentenceTestUserResponse userResponse) =>
            ToActionResult(await _examService.EvaluateSentenceAsync(userResponse));

        private IActionResult ToActionResult(ServiceResult result) =>
            result.IsSuccess
                ? _responseFactory.CreateOKResponse(result.Data!)
                : _responseFactory.CreateErrorResponse(result.Error!.Value, result.ErrorMessage!);
    }
}
