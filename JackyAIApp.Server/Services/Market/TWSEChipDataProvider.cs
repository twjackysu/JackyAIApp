using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.ExternalApi.Twse;

namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// Chip (籌碼面) data from TWSE's OpenAPI datasets (through <see cref="ITwseOpenApi"/>):
    /// margin trading, foreign holdings, short selling, and director holdings.
    /// </summary>
    public class TWSEChipDataProvider : IMarketDataProvider, IChipDataProvider
    {
        private readonly ITwseOpenApi _twse;
        private readonly ILogger<TWSEChipDataProvider> _logger;

        public DataProviderType Type => DataProviderType.Chip;

        public TWSEChipDataProvider(
            ITwseOpenApi twse,
            ILogger<TWSEChipDataProvider> logger)
        {
            _twse = twse ?? throw new ArgumentNullException(nameof(twse));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Chip data is inherently a single current snapshot (margin balances, foreign holding %,
        // ...), not a historical series — no separate shallow variant makes sense here.
        public Task<MarketData> FetchLatestAsync(string stockCode, CancellationToken cancellationToken = default) =>
            FetchAsync(stockCode, cancellationToken);

        public async Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var marketData = new MarketData
            {
                StockCode = stockCode,
                Chips = new ChipData()
            };

            // Run all chip data fetches in parallel
            var tasks = new[]
            {
                FetchMarginDataAsync(stockCode, marketData.Chips, cancellationToken),
                FetchForeignHoldingAsync(stockCode, marketData.Chips, cancellationToken),
                FetchSBLDataAsync(stockCode, marketData.Chips, cancellationToken),
                FetchDirectorHoldingsAsync(stockCode, marketData.Chips, cancellationToken),
                FetchMajorShareholdersAsync(stockCode, marketData.Chips, cancellationToken),
                FetchDayTradingAsync(stockCode, marketData.Chips, cancellationToken)
            };

            await Task.WhenAll(tasks);

            return marketData;
        }

        /// <summary>
        /// Fetches margin trading data (融資融券) from MI_MARGN endpoint.
        /// </summary>
        private async Task FetchMarginDataAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                var data = await _twse.GetMarginTradingAsync(ct);
                if (data == null) return;

                foreach (var item in data)
                {
                    if (item.Code == stockCode)
                    {
                        chipData.MarginBuyVolume = ParseLong(GetJsonString(item, "融資買進"));
                        chipData.MarginSellVolume = ParseLong(GetJsonString(item, "融資賣出"));
                        chipData.MarginCashRepayment = ParseLong(GetJsonString(item, "融資現金償還"));
                        chipData.MarginPreviousBalance = ParseLong(GetJsonString(item, "融資前日餘額"));
                        chipData.MarginBalance = ParseLong(GetJsonString(item, "融資今日餘額"));
                        chipData.MarginLimit = ParseLong(GetJsonString(item, "融資限額"));
                        chipData.ShortBuyVolume = ParseLong(GetJsonString(item, "融券買進"));
                        chipData.ShortSellVolume = ParseLong(GetJsonString(item, "融券賣出"));
                        chipData.ShortCashRepayment = ParseLong(GetJsonString(item, "融券現券償還"));
                        chipData.ShortPreviousBalance = ParseLong(GetJsonString(item, "融券前日餘額"));
                        chipData.ShortBalance = ParseLong(GetJsonString(item, "融券今日餘額"));
                        chipData.ShortLimit = ParseLong(GetJsonString(item, "融券限額"));
                        chipData.OffsetVolume = ParseLong(GetJsonString(item, "資券互抵"));

                        _logger.LogDebug("Fetched margin data for {stockCode}: margin={margin}, short={short}",
                            stockCode, chipData.MarginBalance, chipData.ShortBalance);
                        return;
                    }
                }

                _logger.LogDebug("No margin data found for {stockCode}", stockCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch margin data for {stockCode}", stockCode);
            }
        }

        /// <summary>
        /// Fetches foreign investor holding data from MI_QFIIS_sort_20 and MI_QFIIS_cat.
        /// </summary>
        private async Task FetchForeignHoldingAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                // Try top-20 list first (has per-stock data)
                var top20Data = await _twse.GetForeignHoldingsTop20Async(ct);
                if (top20Data != null)
                {
                    foreach (var item in top20Data)
                    {
                        if (item.Code == stockCode)
                        {
                            chipData.ForeignHoldingShares = ParseLong(GetJsonString(item, "SharesHeld")?.Replace(",", ""));
                            chipData.ForeignHoldingPercentage = ParseDecimal(GetJsonString(item, "SharesHeldPer"));
                            chipData.ForeignAvailableShares = ParseLong(GetJsonString(item, "AvailableShare")?.Replace(",", ""));
                            chipData.ForeignUpperLimit = ParseDecimal(GetJsonString(item, "Upperlimit"));

                            _logger.LogDebug("Fetched foreign holding for {stockCode}: {pct}%",
                                stockCode, chipData.ForeignHoldingPercentage);
                            return;
                        }
                    }
                }

                _logger.LogDebug("Stock {stockCode} not in foreign top-20 list", stockCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch foreign holding data for {stockCode}", stockCode);
            }
        }

        /// <summary>
        /// Fetches securities borrowing and lending (SBL) data from TWT96U.
        /// </summary>
        private async Task FetchSBLDataAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                var data = await _twse.GetSecuritiesLendingAsync(ct);
                if (data == null) return;

                foreach (var item in data)
                {
                    if (item.Code == stockCode)
                    {
                        chipData.SBLAvailableVolume = ParseLong(
                            GetJsonString(item, "TWSEAvailableVolume")?.Replace(",", ""));

                        _logger.LogDebug("Fetched SBL data for {stockCode}: available={vol}",
                            stockCode, chipData.SBLAvailableVolume);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch SBL data for {stockCode}", stockCode);
            }
        }

        /// <summary>
        /// Fetches director/supervisor holdings from t187ap11_L.
        /// </summary>
        private async Task FetchDirectorHoldingsAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                var data = await _twse.GetDirectorHoldingsAsync(ct);
                if (data == null) return;

                var directors = new List<DirectorHolding>();
                long totalPledged = 0;
                long totalHeld = 0;

                foreach (var item in data)
                {
                    if (item.Code == stockCode)
                    {
                        var held = ParseLong(GetJsonString(item, "目前持股"));
                        var pledged = ParseLong(GetJsonString(item, "設質股數"));
                        totalHeld += held;
                        totalPledged += pledged;

                        directors.Add(new DirectorHolding
                        {
                            Title = GetJsonString(item, "職稱") ?? "",
                            Name = GetJsonString(item, "姓名") ?? "",
                            CurrentShares = held,
                            PledgedShares = pledged,
                            PledgeRatio = GetJsonString(item, "設質股數佔持股比例") ?? "0.00%"
                        });
                    }
                }

                if (directors.Count > 0)
                {
                    chipData.DirectorHoldings = directors;
                    chipData.TotalDirectorShares = totalHeld;
                    chipData.TotalDirectorPledged = totalPledged;
                    chipData.DirectorPledgeRatio = totalHeld > 0
                        ? Math.Round((decimal)totalPledged / totalHeld * 100, 2)
                        : 0;

                    _logger.LogDebug("Fetched {count} director holdings for {stockCode}, pledge ratio={ratio}%",
                        directors.Count, stockCode, chipData.DirectorPledgeRatio);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch director holdings for {stockCode}", stockCode);
            }
        }

        /// <summary>
        /// Fetches major shareholders (>10%) from t187ap02_L.
        /// </summary>
        private async Task FetchMajorShareholdersAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                var data = await _twse.GetMajorShareholdersAsync(ct);
                if (data == null) return;

                var shareholders = new List<string>();

                foreach (var item in data)
                {
                    if (item.Code == stockCode)
                    {
                        var name = GetJsonString(item, "大股東名稱");
                        if (!string.IsNullOrEmpty(name))
                        {
                            shareholders.Add(name);
                        }
                    }
                }

                if (shareholders.Count > 0)
                {
                    chipData.MajorShareholders = shareholders;
                    _logger.LogDebug("Found {count} major shareholders for {stockCode}", shareholders.Count, stockCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch major shareholders for {stockCode}", stockCode);
            }
        }

        /// <summary>
        /// Fetches day trading suspension info from TWTB4U.
        /// </summary>
        private async Task FetchDayTradingAsync(string stockCode, ChipData chipData, CancellationToken ct)
        {
            try
            {
                var data = await _twse.GetDayTradingSuspensionsAsync(ct);
                if (data == null) return;

                foreach (var item in data)
                {
                    if (item.Code == stockCode)
                    {
                        var suspension = GetJsonString(item, "Suspension")?.Trim();
                        chipData.DayTradingSuspended = !string.IsNullOrEmpty(suspension);
                        _logger.LogDebug("Day trading status for {stockCode}: suspended={suspended}",
                            stockCode, chipData.DayTradingSuspended);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch day trading data for {stockCode}", stockCode);
            }
        }

        #region Helper Methods

        private static string? GetJsonString(TwseRow row, string field) => row.Value(field);

        private static long ParseLong(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "--" || value == "-")
                return 0;
            return long.TryParse(value.Replace(",", ""), out var result) ? result : 0;
        }

        private static decimal ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "--" || value == "-")
                return 0;
            return decimal.TryParse(value.Replace(",", "").Replace("%", ""), out var result) ? result : 0;
        }

        #endregion
    }
}
