using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>TOEIC Part 2 listening questions (hear a question, pick the reply), under api/Exam.</summary>
    [Authorize]
    [Route("api/Exam")]
    [ApiController]
    public class ListeningResponseExamController(
        IMyResponseFactory responseFactory,
        IExamSpeech speech,
        IListeningResponseExamService listeningResponse) : ExamControllerBase(responseFactory, speech)
    {
        /// <summary>Serves a random/AI-generated TOEIC Part 2 "question + 3 spoken responses"
        /// question. No image, so — unlike Part 1 — this generates live and grows its own bank
        /// (see ListeningResponseExamService.GenerateAsync); flat charge covers the prompt
        /// plus its 3 response options' TTS.</summary>
        [HttpGet("listening/response")]
        [UsageLimit(UsageFeature.ListeningResponseTest)]
        public async Task<IActionResult> GetListeningResponseTest() =>
            ResponseFactory.ToActionResult(await listeningResponse.GenerateAsync());

        /// <summary>Synthesizes speech for a Part 2 question's opening prompt. Unbilled — covered
        /// by the flat "listening/response" charge.</summary>
        [HttpGet("listening/response/prompt-audio")]
        public async Task<IActionResult> GetListeningResponsePromptAudio([FromQuery] string testId) =>
            await SpeakAsync(await listeningResponse.GetPromptTextAsync(testId));

        /// <summary>Synthesizes speech for one Part 2 response option. Unbilled — covered by the
        /// flat "listening/response" charge.</summary>
        [HttpGet("listening/response/option-audio")]
        public async Task<IActionResult> GetListeningResponseOptionAudio([FromQuery] string optionId) =>
            await SpeakAsync(await listeningResponse.GetOptionTextAsync(optionId));
    }
}
