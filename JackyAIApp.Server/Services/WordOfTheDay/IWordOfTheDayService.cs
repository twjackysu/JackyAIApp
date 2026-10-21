using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Services.WordOfTheDay
{
    /// <summary>Provides a daily-rotating random word for the current user.</summary>
    public interface IWordOfTheDayService
    {
        Task<ServiceResult<DTO.Word>> GetWordOfTheDayAsync();
    }
}
