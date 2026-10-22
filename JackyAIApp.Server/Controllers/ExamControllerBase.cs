using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// What the exam controllers share besides their route (<c>api/Exam</c>): answering a text
    /// lookup with its spoken audio (<see cref="IExamSpeech"/>).
    /// </summary>
    public abstract class ExamControllerBase(IMyResponseFactory responseFactory, IExamSpeech speech) : ControllerBase
    {
        protected IMyResponseFactory ResponseFactory { get; } = responseFactory;

        /// <summary>The audio of a looked-up text, or why there is none (the lookup's failure, or
        /// the speech's).</summary>
        protected async Task<IActionResult> SpeakAsync(ServiceResult<string> lookup)
        {
            if (!lookup.IsSuccess)
            {
                return ResponseFactory.ToActionResult(lookup);
            }

            var audio = await speech.SpeakAsync(lookup.Data!);
            return audio.IsSuccess
                ? File(audio.Data!, "audio/mpeg")
                : ResponseFactory.ToActionResult(audio);
        }
    }
}
