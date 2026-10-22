using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>Recording what the user answered, under api/Exam.</summary>
    [Authorize]
    [Route("api/Exam")]
    [ApiController]
    public class ExamResultController(
        IMyResponseFactory responseFactory,
        IExamResultRecorder results) : ControllerBase
    {
        /// <summary>Records an exam outcome to feed spaced repetition + per-word history. Free (no usage charge).</summary>
        [HttpPost("record-result")]
        public async Task<IActionResult> RecordExamResult([FromBody] RecordExamResultRequest request) =>
            responseFactory.ToActionResult(await results.RecordAsync(request));
    }
}
