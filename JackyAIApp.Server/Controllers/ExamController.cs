using Betalgo.Ranul.OpenAI.ObjectModels;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Listening;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ExamController(
        IMyResponseFactory responseFactory,
        IExamService examService,
        ITextToSpeechService ttsService,
        IUserService userService,
        IListeningQuestionGenerator listeningQuestionGenerator,
        AzureSQLDBContext dbContext) : ControllerBase
    {
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IExamService _examService = examService;
        private readonly ITextToSpeechService _ttsService = ttsService;
        private readonly IUserService _userService = userService;
        private readonly IListeningQuestionGenerator _listeningQuestionGenerator = listeningQuestionGenerator;
        private readonly AzureSQLDBContext _dbContext = dbContext;

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

        /// <summary>Generates an end-of-session recap for a conversation (corrections, phrasings, word suggestions).</summary>
        [HttpPost("conversation/summary")]
        [ConsumeCredits(CreditCosts.ConversationSummary, "conversation_summary", "Conversation summary")]
        public async Task<IActionResult> SummarizeConversation([FromBody] ConversationSummaryRequest request) =>
            ToActionResult(await _examService.SummarizeConversationAsync(request));

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

        /// <summary>Records an exam outcome to feed spaced repetition + per-word history. Free (no credits).</summary>
        [HttpPost("record-result")]
        public async Task<IActionResult> RecordExamResult([FromBody] RecordExamResultRequest request) =>
            ToActionResult(await _examService.RecordExamResultAsync(request));

        /// <summary>Serves a random TOEIC Part 1 listening question from the pre-seeded bank. The
        /// flat charge covers both the question and TTS playback of its 4 options.</summary>
        [HttpGet("listening")]
        [ConsumeCredits(CreditCosts.ListeningTest, "listening_test", "Listening test")]
        public async Task<IActionResult> GetListeningTest() =>
            ToActionResult(await _examService.GenerateListeningExamAsync());

        /// <summary>Synthesizes speech for one listening-question option. Unbilled — the flat
        /// "listening" charge already covers this; billing again per option would double-charge
        /// for content the user must listen to just to answer.</summary>
        [HttpGet("listening/audio")]
        public async Task<IActionResult> GetListeningOptionAudio([FromQuery] string optionId)
        {
            var lookup = await _examService.GetListeningOptionTextAsync(optionId);
            if (!lookup.IsSuccess)
            {
                return _responseFactory.CreateErrorResponse(lookup.Error!.Value, lookup.ErrorMessage!);
            }

            var text = (string)lookup.Data!;
            const string voice = "alloy";
            var model = Models.Tts_1;
            const float speed = 1.0f;

            if (_ttsService.TryGetCached(text, voice, model, speed, out var cachedAudio) && cachedAudio != null)
            {
                return File(cachedAudio, "audio/mpeg");
            }

            var result = await _ttsService.SynthesizeAsync(text, voice, model, speed, _userService.GetUserId());
            return result.Success && result.Audio != null
                ? File(result.Audio, "audio/mpeg")
                : _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to generate speech");
        }

        /// <summary>Admin-only: paginated view of the listening question bank.</summary>
        [HttpGet("listening/admin/bank")]
        public async Task<IActionResult> GetListeningBank([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
        {
            if (!await IsAdminAsync())
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "Admin access required.");
            }

            return ToActionResult(await _examService.GetListeningBankAsync(pageNumber, pageSize));
        }

        /// <summary>Admin-only: generates one new listening question (text + photo) via AI and adds
        /// it to the bank. Not credit-gated (admin/back-office action, not a user-facing feature).</summary>
        [HttpPost("listening/admin/generate")]
        public async Task<IActionResult> GenerateListeningQuestion()
        {
            if (!await IsAdminAsync())
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "Admin access required.");
            }

            return ToActionResult(await _listeningQuestionGenerator.GenerateAsync(_userService.GetUserId()));
        }

        private async Task<bool> IsAdminAsync()
        {
            var userId = _userService.GetUserId();
            return !string.IsNullOrEmpty(userId) &&
                await _dbContext.Users.AnyAsync(u => u.Id == userId && u.IsAdmin == true);
        }

        private IActionResult ToActionResult(ServiceResult result) =>
            result.IsSuccess
                ? _responseFactory.CreateOKResponse(result.Data!)
                : _responseFactory.CreateErrorResponse(result.Error!.Value, result.ErrorMessage!);
    }
}
