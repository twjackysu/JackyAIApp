using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.Listening
{
    /// <summary>Admin-only: generates one new Listening question (text via chat model, photo via
    /// image model), persists it to the bank, and returns it.</summary>
    public interface IListeningQuestionGenerator
    {
        Task<ServiceResult<ListeningTestResponse>> GenerateAsync(string? userId);
    }
}
