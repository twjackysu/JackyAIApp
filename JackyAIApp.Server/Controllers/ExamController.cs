using Betalgo.Ranul.OpenAI.ObjectModels;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Listening;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Speech;
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
            _responseFactory.ToActionResult(await _examService.GenerateWordExamAsync(ExamType.Cloze));

        /// <summary>Generates a translation test for the user based on their unfamiliar words.</summary>
        [HttpGet("translation")]
        [ConsumeCredits(CreditCosts.TranslationTest, "translation_test", "Translation test generation")]
        public async Task<IActionResult> GetTranslationTest() =>
            _responseFactory.ToActionResult(await _examService.GenerateWordExamAsync(ExamType.Translation));

        /// <summary>Generates a translation quality grading.</summary>
        [HttpPost("translation/quality_grading")]
        public async Task<IActionResult> GetTranslationQualityGrading([FromBody] TranslationTestUserResponse userResponse) =>
            _responseFactory.ToActionResult(await _examService.GradeTranslationAsync(userResponse));

        /// <summary>Starts a new conversation test by generating a scenario and first message.</summary>
        [HttpPost("conversation/start")]
        [ConsumeCredits(CreditCosts.ConversationStart, "conversation_start", "Conversation test started")]
        public async Task<IActionResult> StartConversationTest([FromBody] ConversationStartRequest request) =>
            _responseFactory.ToActionResult(await _examService.StartConversationAsync(request));

        /// <summary>Processes user's response in a conversation and generates AI response with corrections if needed.</summary>
        [HttpPost("conversation/respond")]
        public async Task<IActionResult> RespondToConversation([FromBody] ConversationResponseRequest request) =>
            _responseFactory.ToActionResult(await _examService.RespondToConversationAsync(request));

        /// <summary>Generates an end-of-session recap for a conversation (corrections, phrasings, word suggestions).</summary>
        [HttpPost("conversation/summary")]
        [ConsumeCredits(CreditCosts.ConversationSummary, "conversation_summary", "Conversation summary")]
        public async Task<IActionResult> SummarizeConversation([FromBody] ConversationSummaryRequest request) =>
            _responseFactory.ToActionResult(await _examService.SummarizeConversationAsync(request));

        /// <summary>Transcribes audio to text using OpenAI Whisper.</summary>
        [HttpPost("whisper/transcribe")]
        public async Task<IActionResult> TranscribeAudio(IFormFile audioFile)
        {
            if (audioFile == null || audioFile.Length == 0)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "No audio file provided.");
            }

            using var stream = audioFile.OpenReadStream();
            return _responseFactory.ToActionResult(await _examService.TranscribeAudioAsync(stream, audioFile.FileName));
        }

        /// <summary>Generates a sentence formation test for the user based on their unfamiliar words.</summary>
        [HttpGet("sentence")]
        [ConsumeCredits(CreditCosts.SentenceTest, "sentence_test", "Sentence test generation")]
        public async Task<IActionResult> GetSentenceTest() =>
            _responseFactory.ToActionResult(await _examService.GenerateWordExamAsync(ExamType.Sentence));

        /// <summary>Evaluates a user's sentence formation response and provides detailed feedback.</summary>
        [HttpPost("sentence/evaluate")]
        public async Task<IActionResult> EvaluateSentence([FromBody] SentenceTestUserResponse userResponse) =>
            _responseFactory.ToActionResult(await _examService.EvaluateSentenceAsync(userResponse));

        /// <summary>Records an exam outcome to feed spaced repetition + per-word history. Free (no credits).</summary>
        [HttpPost("record-result")]
        public async Task<IActionResult> RecordExamResult([FromBody] RecordExamResultRequest request) =>
            _responseFactory.ToActionResult(await _examService.RecordExamResultAsync(request));

        /// <summary>Serves a random TOEIC Part 1 listening question from the pre-seeded bank. The
        /// flat charge covers both the question and TTS playback of its 4 options.</summary>
        [HttpGet("listening")]
        [ConsumeCredits(CreditCosts.ListeningTest, "listening_test", "Listening test")]
        public async Task<IActionResult> GetListeningTest() =>
            _responseFactory.ToActionResult(await _examService.GenerateListeningExamAsync());

        /// <summary>Synthesizes speech for one listening-question option. Unbilled — the flat
        /// "listening" charge already covers this; billing again per option would double-charge
        /// for content the user must listen to just to answer.</summary>
        [HttpGet("listening/audio")]
        public async Task<IActionResult> GetListeningOptionAudio([FromQuery] string optionId)
        {
            var lookup = await _examService.GetListeningOptionTextAsync(optionId);
            return await ToAudioResultAsync(lookup);
        }

        /// <summary>Serves a random/AI-generated TOEIC Part 2 "question + 3 spoken responses"
        /// question. No image, so — unlike Part 1 — this generates live and grows its own bank
        /// (see ExamService.GenerateListeningResponseExamAsync); flat charge covers the prompt
        /// plus its 3 response options' TTS.</summary>
        [HttpGet("listening/response")]
        [ConsumeCredits(CreditCosts.ListeningResponseTest, "listening_response_test", "Listening response test")]
        public async Task<IActionResult> GetListeningResponseTest() =>
            _responseFactory.ToActionResult(await _examService.GenerateListeningResponseExamAsync());

        /// <summary>Synthesizes speech for a Part 2 question's opening prompt. Unbilled — covered
        /// by the flat "listening/response" charge.</summary>
        [HttpGet("listening/response/prompt-audio")]
        public async Task<IActionResult> GetListeningResponsePromptAudio([FromQuery] string testId)
        {
            var lookup = await _examService.GetListeningResponsePromptTextAsync(testId);
            return await ToAudioResultAsync(lookup);
        }

        /// <summary>Synthesizes speech for one Part 2 response option. Unbilled — covered by the
        /// flat "listening/response" charge.</summary>
        [HttpGet("listening/response/option-audio")]
        public async Task<IActionResult> GetListeningResponseOptionAudio([FromQuery] string optionId)
        {
            var lookup = await _examService.GetListeningResponseOptionTextAsync(optionId);
            return await ToAudioResultAsync(lookup);
        }

        /// <summary>Serves a short reading passage with 3-4 comprehension questions from the
        /// shared bank. Not tied to the user's saved words — works with an empty repository.</summary>
        [HttpGet("reading")]
        [ConsumeCredits(CreditCosts.ReadingComprehensionTest, "reading_test", "Reading comprehension test")]
        public async Task<IActionResult> GetReadingTest() =>
            _responseFactory.ToActionResult(await _examService.GenerateReadingExamAsync());

        /// <summary>Serves an open-ended writing prompt (topic + up to 5 of the user's own saved
        /// words). Deliberately NOT credit-gated: unlike every other exam-type "generation"
        /// endpoint, this one costs zero AI calls to produce (plain DB queries) — the whole
        /// charge for this feature sits on POST writing/evaluate instead. Do not add
        /// [ConsumeCredits] here.</summary>
        [HttpGet("writing")]
        public async Task<IActionResult> GetWritingPrompt() =>
            _responseFactory.ToActionResult(await _examService.GenerateWritingPromptAsync());

        /// <summary>Grades an open-ended writing submission and provides detailed feedback.</summary>
        [HttpPost("writing/evaluate")]
        public async Task<IActionResult> EvaluateWriting([FromBody] WritingSubmissionRequest request) =>
            _responseFactory.ToActionResult(await _examService.GradeWritingAsync(request));

        /// <summary>Generates a dictation prompt: picks one of the user's words and returns only
        /// its WordId (see DictationPromptResponse — the text is deliberately withheld, unlike
        /// Cloze/Translation's GET response). Flat charge covers the one TTS call the user is
        /// expected to make via dictation/audio right after.</summary>
        [HttpGet("dictation")]
        [ConsumeCredits(CreditCosts.DictationTest, "dictation_test", "Dictation test generation")]
        public async Task<IActionResult> GetDictationTest() =>
            _responseFactory.ToActionResult(await _examService.GenerateDictationExamAsync());

        /// <summary>Synthesizes speech for the dictation word. Unbilled — the flat "dictation"
        /// charge above already covers this.</summary>
        [HttpGet("dictation/audio")]
        public async Task<IActionResult> GetDictationWordAudio([FromQuery] string wordId)
        {
            var lookup = await _examService.GetDictationWordTextAsync(wordId);
            return await ToAudioResultAsync(lookup);
        }

        /// <summary>Grades a dictation submission against the real word text looked up
        /// server-side. Deliberately NOT credit-gated — zero underlying cost (pure in-memory
        /// string comparison), same reasoning as GetWritingPrompt. Do not add [ConsumeCredits]
        /// here.</summary>
        [HttpPost("dictation/grade")]
        public async Task<IActionResult> GradeDictation([FromBody] DictationGradeRequest request) =>
            _responseFactory.ToActionResult(await _examService.GradeDictationAsync(request));

        /// <summary>Shared unbilled-TTS-lookup helper: resolves a ServiceResult&lt;string&gt; text
        /// lookup into a cached-or-freshly-synthesized audio response.</summary>
        private async Task<IActionResult> ToAudioResultAsync(ServiceResult lookup)
        {
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

            return _responseFactory.ToActionResult(await _examService.GetListeningBankAsync(pageNumber, pageSize));
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

            return _responseFactory.ToActionResult(await _listeningQuestionGenerator.GenerateAsync(_userService.GetUserId()));
        }

        private async Task<bool> IsAdminAsync()
        {
            var userId = _userService.GetUserId();
            return !string.IsNullOrEmpty(userId) &&
                await _dbContext.Users.AnyAsync(u => u.Id == userId && u.IsAdmin == true);
        }
    }
}
