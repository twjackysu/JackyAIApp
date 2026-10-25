using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.Assistant.Memory
{
    /// <summary>
    /// The signed-in reader's memories as the connectors page shows them (docs/ai-native/design/08 §6): listed, and
    /// forgotten one at a time. Someone else's memory is NotFound.
    /// </summary>
    public interface IAssistantMemoryPage
    {
        Task<ServiceResult<AssistantMemoryListDto>> ListAsync(CancellationToken ct);

        Task<ServiceResult<NoData>> ForgetAsync(string id, CancellationToken ct);
    }

    public sealed class AssistantMemoryPage(IAssistantMemoryService memories, ICurrentUser currentUser) : IAssistantMemoryPage
    {
        public async Task<ServiceResult<AssistantMemoryListDto>> ListAsync(CancellationToken ct) =>
            ServiceResult.Ok(new AssistantMemoryListDto(
                await memories.ListAsync(currentUser.RequireId(), ct), AssistantMemoryService.Limit));

        public async Task<ServiceResult<NoData>> ForgetAsync(string id, CancellationToken ct) =>
            await memories.RemoveAsync(currentUser.RequireId(), id, ct) == null
                ? ServiceResult.Fail(ErrorCodes.NotFound, "找不到這一則。")
                : ServiceResult.Ok();
    }
}
