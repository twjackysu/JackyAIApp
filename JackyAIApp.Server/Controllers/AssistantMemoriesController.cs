using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Assistant.Memory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// What the reader asked Studio AI to remember, on the connectors page (docs/ai-native/design/08 §6): listed and
    /// forgotten one at a time. Keeping one is Studio AI's <c>remember</c>, asked first in the chat. In the
    /// <c>{ data }</c> envelope.
    /// </summary>
    [ApiController]
    [Route("api/assistant/memories")]
    [Authorize]
    public class AssistantMemoriesController(IAssistantMemoryPage memories, IMyResponseFactory responseFactory) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct = default) =>
            responseFactory.ToActionResult(await memories.ListAsync(ct));

        [HttpDelete("{id}")]
        public async Task<IActionResult> Forget(string id, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await memories.ForgetAsync(id, ct));
    }
}
