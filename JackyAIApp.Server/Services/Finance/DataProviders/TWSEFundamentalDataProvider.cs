using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using System.Globalization;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Fundamental data from TWSE's OpenAPI datasets (through <see cref="ITwseOpenApi"/>):
    /// - BWIBBU_d: P/E ratio, P/B ratio, Dividend Yield
    /// - t187ap05_L: Monthly revenue, YoY, MoM
    /// - t187ap14_L: EPS (latest reported quarter)
    /// - STOCK_DAY_ALL: the latest close, for an EPS derived from P/E
    /// </summary>
    public class TWSEFundamentalDataProvider : IFundamentalDataProvider
    {
        private readonly ILogger<TWSEFundamentalDataProvider> _logger;
        private readonly ITwseOpenApi _twse;

        public TWSEFundamentalDataProvider(
            ILogger<TWSEFundamentalDataProvider> logger,
            ITwseOpenApi twse)
        {
            _logger = logger;
            _twse = twse;
        }

        public async Task<FundamentalData?> FetchAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var result = new FundamentalData();
            var hasData = false;

            // Fetch all sources in parallel
            var peTask = FetchValuationData(stockCode, cancellationToken);
            var revenueTask = FetchRevenueData(stockCode, cancellationToken);
            var epsTask = FetchEPSData(stockCode, cancellationToken);
            var priceTask = FetchLatestPrice(stockCode, cancellationToken);

            await Task.WhenAll(peTask, revenueTask, epsTask, priceTask);

            // Merge P/E, P/B, Dividend Yield
            var valuation = await peTask;
            if (valuation != null)
            {
                result.PERatio = valuation.PERatio;
                result.PBRatio = valuation.PBRatio;
                result.DividendYield = valuation.DividendYield;
                result.FiscalYearQuarter = valuation.FiscalYearQuarter;
                hasData = true;
            }

            // Merge Revenue
            var revenue = await revenueTask;
            if (revenue != null)
            {
                result.MonthlyRevenue = revenue.MonthlyRevenue;
                result.RevenueYoY = revenue.RevenueYoY;
                result.RevenueMoM = revenue.RevenueMoM;
                result.RevenueMonth = revenue.RevenueMonth;
                result.RevenueLabel = revenue.RevenueLabel;
                hasData = true;
            }

            // Merge EPS — prefer direct report, fallback to Price/PE derived
            var eps = await epsTask;
            if (eps != null)
            {
                result.EPS = eps.EPS;
                result.OperatingIncome = eps.OperatingIncome;
                result.NetIncome = eps.NetIncome;
                result.EpsDataPeriod = eps.EpsDataPeriod;
                hasData = true;
            }
            else if (result.PERatio > 0)
            {
                // Derive trailing EPS from closing price and P/E ratio
                var latestPrice = await priceTask;
                if (latestPrice > 0)
                {
                    result.TrailingEPS = Math.Round(latestPrice / result.PERatio.Value, 2);
                    result.EPS = result.TrailingEPS;
                    hasData = true;
                    _logger.LogInformation("Derived EPS for {stockCode}: {eps} (price={price}, PE={pe})",
                        stockCode, result.TrailingEPS, latestPrice, result.PERatio);
                }
            }

            return hasData ? result : null;
        }

        private async Task<FundamentalData?> FetchValuationData(string stockCode, CancellationToken ct)
        {
            try
            {
                var stock = (await _twse.GetDailyValuationsAsync(ct))?.FirstOrDefault(r => r.Code == stockCode);
                if (stock == null) return null;

                return new FundamentalData
                {
                    PERatio = ParseDecimal(stock, "PEratio"),
                    PBRatio = ParseDecimal(stock, "PBratio"),
                    DividendYield = ParseDecimal(stock, "DividendYield"),
                    FiscalYearQuarter = stock.Value("FiscalYearQuarter")
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch valuation data for {stockCode}", stockCode);
                return null;
            }
        }

        private async Task<FundamentalData?> FetchRevenueData(string stockCode, CancellationToken ct)
        {
            try
            {
                var stock = (await _twse.GetMonthlyRevenuesAsync(ct))?.FirstOrDefault(r => r.Code == stockCode);
                if (stock == null) return null;

                var monthlyRevenue = stock.Revenue;
                var revenueMonth = stock.DataMonth;

                // Build label: 月營收=XXX百萬（11501）
                string? revenueLabel = null;
                if (monthlyRevenue.HasValue)
                {
                    var millions = monthlyRevenue.Value / 1000;
                    var periodSuffix = !string.IsNullOrEmpty(revenueMonth) ? $"（{revenueMonth}）" : "";
                    revenueLabel = $"月營收={millions:F0}百萬{periodSuffix}";
                }

                return new FundamentalData
                {
                    MonthlyRevenue = monthlyRevenue,
                    RevenueYoY = stock.RevenueYoY,
                    RevenueMoM = stock.RevenueMoM,
                    RevenueMonth = revenueMonth,
                    RevenueLabel = revenueLabel
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch revenue data for {stockCode}", stockCode);
                return null;
            }
        }

        private async Task<FundamentalData?> FetchEPSData(string stockCode, CancellationToken ct)
        {
            try
            {
                var stock = (await _twse.GetQuarterlyEpsAsync(ct))?.FirstOrDefault(r => r.Code == stockCode);
                if (stock == null) return null;

                // Build EPS data period from 年度 + 季別 fields
                var year = stock.Value("年度")?.Trim();
                var quarter = stock.Value("季別")?.Trim();
                string? epsPeriod = (year, quarter) switch
                {
                    (not null, not null) => $"{year}年Q{quarter}",
                    (not null, _) => $"{year}年",
                    _ => null
                };

                return new FundamentalData
                {
                    EPS = ParseDecimalFromChinese(stock, "基本每股盈餘(元)"),
                    OperatingIncome = ParseDecimalFromChinese(stock, "營業利益"),
                    NetIncome = ParseDecimalFromChinese(stock, "稅後淨利"),
                    EpsDataPeriod = epsPeriod
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch EPS data for {stockCode}", stockCode);
                return null;
            }
        }

        private async Task<decimal> FetchLatestPrice(string stockCode, CancellationToken ct)
        {
            try
            {
                var quote = (await _twse.GetDailyQuotesAsync(ct))?.FirstOrDefault(q => q.Code == stockCode);
                return quote?.ClosingPrice ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch latest price for {stockCode}", stockCode);
                return 0;
            }
        }

        private static decimal? ParseDecimal(TwseRow row, string field)
        {
            var str = row.Value(field)?.Trim();
            if (string.IsNullOrEmpty(str) || str == "-" || str == "N/A") return null;
            return decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : null;
        }

        private static decimal? ParseDecimalFromChinese(TwseRow row, string field)
        {
            var str = row.Value(field)?.Trim();
            if (string.IsNullOrEmpty(str) || str == "-" || str == "N/A") return null;
            // Remove commas and whitespace
            str = str.Replace(",", "").Replace(" ", "");
            return decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : null;
        }
    }
}
