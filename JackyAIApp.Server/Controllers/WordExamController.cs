using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>The vocabulary-based exams (cloze, translation, sentence formation) and their grading, under api/Exam.</summary>
    [Authorize]
    [Route("api/Exam")]
    [ApiController]
    public class WordExamController(
        IMyResponseFactory responseFactory,
        IWordExamService wordExams) : ControllerBase
    {
        /// <summary>Generates a cloze test for the user based on their unfamiliar words.</summary>
        [HttpGet("cloze")]
        [UsageLimit(UsageFeature.ClozeTest)]
        public async Task<IActionResult> GetClozeTest() =>
            responseFactory.ToActionResult(await wordExams.GenerateClozeAsync());

        /// <summary>Generates a translation test for the user based on their unfamiliar words.</summary>
        [HttpGet("translation")]
        [UsageLimit(UsageFeature.TranslationTest)]
        public async Task<IActionResult> GetTranslationTest() =>
            responseFactory.ToActionResult(await wordExams.GenerateTranslationAsync());

        /// <summary>Generates a translation quality grading.</summary>
        [HttpPost("translation/quality_grading")]
        [UsageLimit(UsageFeature.TranslationGrading)]
        public async Task<IActionResult> GetTranslationQualityGrading([FromBody] TranslationTestUserResponse userResponse) =>
            responseFactory.ToActionResult(await wordExams.GradeTranslationAsync(userResponse));

        /// <summary>Generates a sentence formation test for the user based on their unfamiliar words.</summary>
        [HttpGet("sentence")]
        [UsageLimit(UsageFeature.SentenceTest)]
        public async Task<IActionResult> GetSentenceTest() =>
            responseFactory.ToActionResult(await wordExams.GenerateSentenceAsync());

        /// <summary>Evaluates a user's sentence formation response and provides detailed feedback.</summary>
        [HttpPost("sentence/evaluate")]
        [UsageLimit(UsageFeature.SentenceEvaluation)]
        public async Task<IActionResult> EvaluateSentence([FromBody] SentenceTestUserResponse userResponse) =>
            responseFactory.ToActionResult(await wordExams.EvaluateSentenceAsync(userResponse));
    }
}
