using JackyAIApp.Server.DTO.Finance;
using TWStockLib.Models;
using TWStockLib.Services;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Fetches historical price data using TWStockLib NuGet package.
    /// Provides OHLCV data for technical indicator calculations.
    /// </summary>
    public class TWStockLibHistoricalProvider : IMarketDataProvider
    {
        private readonly IStockMarketService _stockService;
        private readonly ILogger<TWStockLibHistoricalProvider> _logger;

        /// <summary>Default lookback period in months for historical data</summary>
        private const int DEFAULT_LOOKBACK_MONTHS = 6;

        public DataProviderType Type => DataProviderType.HistoricalPrice;

        public TWStockLibHistoricalProvider(
            IStockMarketService stockService,
            ILogger<TWStockLibHistoricalProvider> logger)
        {
            _stockService = stockService ?? throw new ArgumentNullException(nameof(stockService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var marketData = new MarketData { StockCode = stockCode };

            try
            {
                var endDate = DateTime.Now;
                var startDate = endDate.AddMonths(-DEFAULT_LOOKBACK_MONTHS);

                // Determine market type (TSE or OTC)
                var marketType = await DetermineMarketType(stockCode);

                _logger.LogInformation(
                    "Fetching historical data for {stockCode} from {start:yyyy-MM-dd} to {end:yyyy-MM-dd} on {market}",
                    stockCode, startDate, endDate, marketType);

                var historyResult = await _stockService.GetHistoricalDataAsync(
                    stockCode, startDate, endDate, marketType, cancellationToken);

                if (!historyResult.IsSuccess)
                {
                    _logger.LogWarning(
                        "GetHistoricalDataAsync failed for {stockCode}: {errorCode} - {errorMessage}",
                        stockCode, historyResult.ErrorCode, historyResult.ErrorMessage);
                    return marketData;
                }

                var historyList = historyResult.Value!
                    .Where(h => h.ClosingPrice > 0) // Filter out invalid entries
                    .OrderBy(h => h.Date)
                    .ToList();

                if (historyList.Count == 0)
                {
                    _logger.LogWarning("No historical data returned for stock {stockCode}", stockCode);
                    return marketData;
                }

                marketData.HistoricalPrices = historyList.Select(h => new DailyPrice
                {
                    Date = h.Date,
                    Open = h.OpeningPrice,
                    High = h.HighestPrice,
                    Low = h.LowestPrice,
                    Close = h.ClosingPrice,
                    Volume = h.TradeVolume,
                    Turnover = (long)h.TurnOverInValue,
                    Transactions = (int)h.NumberOfDeals
                }).ToList();

                // Try to get company name from realtime quote
                try
                {
                    var quoteResult = await _stockService.GetRealtimeQuoteAsync(stockCode, marketType, cancellationToken);
                    if (quoteResult.IsSuccess)
                    {
                        marketData.CompanyName = quoteResult.Value!.Name;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not fetch realtime quote for company name, skipping");
                }

                _logger.LogInformation(
                    "Fetched {count} historical records for {stockCode} ({name})",
                    marketData.HistoricalPrices.Count, stockCode, marketData.CompanyName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch historical data for stock {stockCode}", stockCode);
            }

            return marketData;
        }

        /// <summary>
        /// Determines whether a stock is on TSE or OTC market.
        /// Tries TSE first (majority of stocks), falls back to OTC.
        /// </summary>
        private async Task<MarketType> DetermineMarketType(string stockCode)
        {
            try
            {
                var tseResult = await _stockService.GetStockListAsync(MarketType.TSE);
                if (tseResult.IsSuccess && tseResult.Value!.ContainsKey(stockCode))
                    return MarketType.TSE;

                var otcResult = await _stockService.GetStockListAsync(MarketType.OTC);
                if (otcResult.IsSuccess && otcResult.Value!.ContainsKey(stockCode))
                    return MarketType.OTC;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to determine market type, defaulting to TSE");
            }

            // Default to TSE for most cases
            return MarketType.TSE;
        }
    }
}
