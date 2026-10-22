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
        IExamSpeech speech,
        IExamService examService) : ExamControllerBase(responseFactory, speech)
    {
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IExamService _examService = examService;

        /// <summary>Generates a cloze test for the user based on their unfamiliar words.</summary>
        [HttpGet("cloze")]
        [UsageLimit(UsageFeature.ClozeTest)]
        public async Task<IActionResult> GetClozeTest() =>
            _responseFactory.ToActionResult(await _examService.GenerateWordExamAsync(ExamType.Cloze));

        /// <summary>Generates a translation test for the user based on their unfamiliar words.</summary>
        [HttpGet("translation")]
        [UsageLimit(UsageFeature.TranslationTest)]
        public async Task<IActionResult> GetTranslationTest() =>
            _responseFactory.ToActionResult(await _examService.GenerateWordExamAsync(ExamType.Translation));

        /// <summary>Generates a translation quality grading.</summary>
        [HttpPost("translation/quality_grading")]
        [UsageLimit(UsageFeature.TranslationGrading)]
        public async Task<IActionResult> GetTranslationQualityGrading([FromBody] TranslationTestUserResponse userResponse) =>
            _responseFactory.ToActionResult(await _examService.GradeTranslationAsync(userResponse));

        /// <summary>Starts a new conversation test by generating a scenario and first message.</summary>
        [HttpPost("conversation/start")]
        [UsageLimit(UsageFeature.ConversationStart)]
        public async Task<IActionResult> StartConversationTest([FromBody] ConversationStartRequest request) =>
            _responseFactory.ToActionResult(await _examService.StartConversationAsync(request));

        /// <summary>Processes user's response in a conversation and generates AI response with corrections if needed.</summary>
        [HttpPost("conversation/respond")]
        [UsageLimit(UsageFeature.ConversationResponse)]
        public async Task<IActionResult> RespondToConversation([FromBody] ConversationResponseRequest request) =>
            _responseFactory.ToActionResult(await _examService.RespondToConversationAsync(request));

        /// <summary>Generates an end-of-session recap for a conversation (corrections, phrasings, word suggestions).</summary>
        [HttpPost("conversation/summary")]
        [UsageLimit(UsageFeature.ConversationSummary)]
        public async Task<IActionResult> SummarizeConversation([FromBody] ConversationSummaryRequest request) =>
            _responseFactory.ToActionResult(await _examService.SummarizeConversationAsync(request));

        /// <summary>Transcribes audio to text using OpenAI Whisper.</summary>
        [HttpPost("whisper/transcribe")]
        [UsageLimit(UsageFeature.AudioTranscription)]
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
        [UsageLimit(UsageFeature.SentenceTest)]
        public async Task<IActionResult> GetSentenceTest() =>
            _responseFactory.ToActionResult(await _examService.GenerateWordExamAsync(ExamType.Sentence));

        /// <summary>Evaluates a user's sentence formation response and provides detailed feedback.</summary>
        [HttpPost("sentence/evaluate")]
        [UsageLimit(UsageFeature.SentenceEvaluation)]
        public async Task<IActionResult> EvaluateSentence([FromBody] SentenceTestUserResponse userResponse) =>
            _responseFactory.ToActionResult(await _examService.EvaluateSentenceAsync(userResponse));

        /// <summary>Records an exam outcome to feed spaced repetition + per-word history. Free (no usage charge).</summary>
        [HttpPost("record-result")]
        public async Task<IActionResult> RecordExamResult([FromBody] RecordExamResultRequest request) =>
            _responseFactory.ToActionResult(await _examService.RecordExamResultAsync(request));

        /// <summary>Serves an open-ended writing prompt (topic + up to 5 of the user's own saved
        /// words). Deliberately NOT usage-limit-gated: unlike every other exam-type "generation"
        /// endpoint, this one costs zero AI calls to produce (plain DB queries) — the whole
        /// charge for this feature sits on POST writing/evaluate instead. Do not add
        /// [UsageLimit] here.</summary>
        [HttpGet("writing")]
        public async Task<IActionResult> GetWritingPrompt() =>
            _responseFactory.ToActionResult(await _examService.GenerateWritingPromptAsync());

        /// <summary>Grades an open-ended writing submission and provides detailed feedback.</summary>
        [HttpPost("writing/evaluate")]
        [UsageLimit(UsageFeature.WritingEvaluation)]
        public async Task<IActionResult> EvaluateWriting([FromBody] WritingSubmissionRequest request) =>
            _responseFactory.ToActionResult(await _examService.GradeWritingAsync(request));

        /// <summary>Generates a dictation prompt: picks a word via the same due/weak/global-fallback
        /// logic as Cloze/Translation/Sentence, but returns only its WordId — the word text is
        /// deliberately withheld (unlike Cloze/Translation) since guessing it from audio alone is
        /// the whole exercise. Flat charge covers the one TTS call the user is expected to make
        /// right after.</summary>
        [HttpGet("dictation")]
        [UsageLimit(UsageFeature.DictationTest)]
        public async Task<IActionResult> GetDictationTest() =>
            _responseFactory.ToActionResult(await _examService.GenerateDictationExamAsync());

        /// <summary>Synthesizes speech for a dictation word. Unbilled — the flat "dictation"
        /// charge already covers this.</summary>
        [HttpGet("dictation/audio")]
        public async Task<IActionResult> GetDictationWordAudio([FromQuery] string wordId)
        {
            var lookup = await _examService.GetDictationWordTextAsync(wordId);
            return await SpeakAsync(lookup);
        }

        /// <summary>Grades a dictation submission. Deliberately NOT usage-limit-gated: pure in-memory
        /// string comparison against the server-resolved word text, no AI call, no TTS call —
        /// same "zero underlying cost" reasoning as GET api/exam/writing. Do not add
        /// [UsageLimit] here.</summary>
        [HttpPost("dictation/grade")]
        public async Task<IActionResult> GradeDictation([FromBody] DictationGradeRequest request) =>
            _responseFactory.ToActionResult(await _examService.GradeDictationAsync(request));
    }
}
