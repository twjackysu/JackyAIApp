using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// The AI-backed market reads that cost usage: the daily "今日市場AI摘要" digest (charged once
    /// per user per day) and the lazy per-indicator insight. Both ground gpt-6-luna in data that
    /// <see cref="IMarketDataQueryService"/> already caches.
    /// </summary>
    public interface IMarketInsightService
    {
        Task<ServiceResult> GetMarketDigestAsync(CancellationToken cancellationToken);
        Task<ServiceResult> GetIndicatorInsightAsync(string id, CancellationToken cancellationToken);
    }
}
