using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Listening;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>TOEIC Part 1 listening questions (look at the picture, pick the description), under api/Exam.</summary>
    [Authorize]
    [Route("api/Exam")]
    [ApiController]
    public class ListeningExamController(
        IMyResponseFactory responseFactory,
        IExamSpeech speech,
        IListeningExamService listening,
        IListeningQuestionGenerator listeningQuestionGenerator,
        ICurrentUser currentUser) : ExamControllerBase(responseFactory, speech)
    {
        /// <summary>Serves a random TOEIC Part 1 listening question from the pre-seeded bank. The
        /// flat charge covers both the question and TTS playback of its 4 options.</summary>
        [HttpGet("listening")]
        [UsageLimit(UsageFeature.ListeningTest)]
        public async Task<IActionResult> GetListeningTest() =>
            ResponseFactory.ToActionResult(await listening.GenerateAsync());

        /// <summary>Synthesizes speech for one listening-question option. Unbilled — the flat
        /// "listening" charge already covers this; billing again per option would double-charge
        /// for content the user must listen to just to answer.</summary>
        [HttpGet("listening/audio")]
        public async Task<IActionResult> GetListeningOptionAudio([FromQuery] string optionId) =>
            await SpeakAsync(await listening.GetOptionTextAsync(optionId));

        /// <summary>Admin-only: paginated view of the listening question bank.</summary>
        [AdminOnly]
        [HttpGet("listening/admin/bank")]
        public async Task<IActionResult> GetListeningBank([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20) =>
            ResponseFactory.ToActionResult(await listening.GetBankAsync(pageNumber, pageSize));

        /// <summary>Admin-only: generates one new listening question (text + photo) via AI and adds
        /// it to the bank. Not usage-limit-gated (admin/back-office action, not a user-facing feature).</summary>
        [AdminOnly]
        [HttpPost("listening/admin/generate")]
        public async Task<IActionResult> GenerateListeningQuestion() =>
            ResponseFactory.ToActionResult(await listeningQuestionGenerator.GenerateAsync(currentUser.Id));
    }
}
