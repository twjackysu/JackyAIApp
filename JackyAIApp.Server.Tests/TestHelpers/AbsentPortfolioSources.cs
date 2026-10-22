using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// Data sources a Portfolio test doesn't exercise, answering as if they had nothing to say —
    /// which is what the services used to do when one was left out (null) instead of passed.
    /// </summary>
    internal static class AbsentPortfolioSources
    {
        /// <summary>No 集保 rate: a fund converts at the mid-market rate.</summary>
        public static readonly ITdccFxService TdccFx = new NoTdccFx();

        /// <summary>No 國泰世華 rate: a US stock converts at the mid-market rate.</summary>
        public static readonly ICathayFxService CathayFx = new NoCathayFx();

        /// <summary>No stored older history: the long-range charts keep to the ~6 months of prices
        /// the providers hand out.</summary>
        public static readonly IDeepHistoryService DeepHistory = new NoDeepHistory();

        /// <summary>No batch of quotes: each TW stock is quoted on its own.</summary>
        public static readonly ITwQuotePrefetcher TwQuotes = new NoTwQuotes();

        private sealed class NoTwQuotes : ITwQuotePrefetcher
        {
            public Task PrefetchAsync(IReadOnlyCollection<string> stockCodes, CancellationToken ct) => Task.CompletedTask;
        }

        private sealed class NoTdccFx : ITdccFxService
        {
            public Task<decimal?> GetBuyRateAsync(string currency, DateOnly asOf, CancellationToken ct = default) =>
                Task.FromResult<decimal?>(null);

            public Task<IReadOnlyDictionary<DateOnly, decimal>> GetBuyRateSeriesAsync(
                string currency, DateOnly from, DateOnly to, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyDictionary<DateOnly, decimal>>(new Dictionary<DateOnly, decimal>());
        }

        private sealed class NoCathayFx : ICathayFxService
        {
            public Task<decimal?> GetBuyRateAsync(string currency, DateOnly rateDate, CancellationToken ct = default) =>
                Task.FromResult<decimal?>(null);
        }

        private sealed class NoDeepHistory : IDeepHistoryService
        {
            public Task<DeepHistoryResult> ExtendAsync(
                IReadOnlyDictionary<string, MarketData> stocks, IReadOnlyList<HistoryWant> wants,
                DateOnly targetFrom, CancellationToken ct) =>
                Task.FromResult(new DeepHistoryResult(new Dictionary<string, MarketData>(stocks), null));
        }
    }
}
