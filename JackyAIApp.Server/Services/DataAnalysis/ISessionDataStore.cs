using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Temporary in-memory data source for uploaded tabular data (strings only).
    /// Sessions are cached for a couple of hours and never persisted to disk; all
    /// query operations run in-memory over the uploaded rows.
    /// </summary>
    public interface ISessionDataStore
    {
        ServiceResult Upload(SessionUploadDto dto);
        ServiceResult GetSchema(string sessionId);
        ServiceResult GetMaxDate(string sessionId, string dateField);
        ServiceResult GetDistinctValues(string sessionId, string field);
        ServiceResult Summarize(string sessionId, SummarizeRequestDto dto);
        ServiceResult Details(string sessionId, DetailsRequestDto dto);
    }
}
