using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Provider for macro economy data (market index, sectors, margin, FX rates).
    /// </summary>
    public interface IMacroEconomyProvider
    {
        Task<MacroEconomyResponse> FetchAsync(CancellationToken cancellationToken = default);
    }
}
