using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>Dictation practice, under api/Exam.</summary>
    [Authorize]
    [Route("api/Exam")]
    [ApiController]
    public class DictationExamController(
        IMyResponseFactory responseFactory,
        IExamSpeech speech,
        IDictationExamService dictation) : ExamControllerBase(responseFactory, speech)
    {
        /// <summary>Generates a dictation prompt: picks a word via the same due/weak/global-fallback
        /// logic as Cloze/Translation/Sentence, but returns only its WordId — the word text is
        /// deliberately withheld (unlike Cloze/Translation) since guessing it from audio alone is
        /// the whole exercise. Flat charge covers the one TTS call the user is expected to make
        /// right after.</summary>
        [HttpGet("dictation")]
        [UsageLimit(UsageFeature.DictationTest)]
        public async Task<IActionResult> GetDictationTest() =>
            ResponseFactory.ToActionResult(await dictation.GenerateAsync());

        /// <summary>Synthesizes speech for a dictation word. Unbilled — the flat "dictation"
        /// charge already covers this.</summary>
        [HttpGet("dictation/audio")]
        public async Task<IActionResult> GetDictationWordAudio([FromQuery] string wordId)
        {
            var lookup = await dictation.GetWordTextAsync(wordId);
            return await SpeakAsync(lookup);
        }

        /// <summary>Grades a dictation submission. Deliberately NOT usage-limit-gated: pure in-memory
        /// string comparison against the server-resolved word text, no AI call, no TTS call —
        /// same "zero underlying cost" reasoning as GET api/exam/writing. Do not add
        /// [UsageLimit] here.</summary>
        [HttpPost("dictation/grade")]
        public async Task<IActionResult> GradeDictation([FromBody] DictationGradeRequest request) =>
            ResponseFactory.ToActionResult(await dictation.GradeAsync(request));
    }
}
