using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>Writing practice, under api/Exam.</summary>
    [Authorize]
    [Route("api/Exam")]
    [ApiController]
    public class WritingExamController(
        IMyResponseFactory responseFactory,
        IWritingExamService writing) : ControllerBase
    {
        /// <summary>Serves an open-ended writing prompt (topic + up to 5 of the user's own saved
        /// words). Deliberately NOT usage-limit-gated: unlike every other exam-type "generation"
        /// endpoint, this one costs zero AI calls to produce (plain DB queries) — the whole
        /// charge for this feature sits on POST writing/evaluate instead. Do not add
        /// [UsageLimit] here.</summary>
        [HttpGet("writing")]
        public async Task<IActionResult> GetWritingPrompt() =>
            responseFactory.ToActionResult(await writing.GeneratePromptAsync());

        /// <summary>Grades an open-ended writing submission and provides detailed feedback.</summary>
        [HttpPost("writing/evaluate")]
        [UsageLimit(UsageFeature.WritingEvaluation)]
        public async Task<IActionResult> EvaluateWriting([FromBody] WritingSubmissionRequest request) =>
            responseFactory.ToActionResult(await writing.GradeAsync(request));
    }
}
