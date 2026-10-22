using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>Reading comprehension passages, under api/Exam.</summary>
    [Authorize]
    [Route("api/Exam")]
    [ApiController]
    public class ReadingExamController(IMyResponseFactory responseFactory, IReadingExamService reading) : ControllerBase
    {
        /// <summary>Serves a short reading passage with 3-4 comprehension questions from the
        /// shared bank. Not tied to the user's saved words — works with an empty repository.</summary>
        [HttpGet("reading")]
        [UsageLimit(UsageFeature.ReadingComprehensionTest)]
        public async Task<IActionResult> GetReadingTest() =>
            responseFactory.ToActionResult(await reading.GenerateAsync());
    }
}
