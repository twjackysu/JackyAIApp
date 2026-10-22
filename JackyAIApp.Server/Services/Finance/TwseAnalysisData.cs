using System.Text;
using JackyAIApp.Server.Services.ExternalApi.Twse;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// TWSE's data on one stock, written out for the AI stock analysis
    /// (<see cref="StockAnalysisQueryService"/>): each dataset's row for the stock, field by field,
    /// grouped by the timeframe it informs. The datasets come from <see cref="ITwseOpenApi"/>, which
    /// downloads each once for every stock.
    /// </summary>
    public interface ITwseAnalysisData
    {
        Task<string> DescribeAsync(string stockCode, CancellationToken cancellationToken = default);
    }

    public class TwseAnalysisData(ITwseOpenApi twse, ILogger<TwseAnalysisData> logger) : ITwseAnalysisData
    {
        private readonly ITwseOpenApi _twse = twse;
        private readonly ILogger<TwseAnalysisData> _logger = logger;

        /// <summary>The market-index section is context, not the subject: as many index rows as fit in this.</summary>
        private const int MarketIndicesBudget = 1000;

        public async Task<string> DescribeAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            try
            {
                // Launch every section in parallel. Each dataset is market-wide and shared across all
                // stocks, so the first call of the day pays the download and the rest are cache hits.
                var dailyTradingTask = _twse.GetDailyQuotesAsync(cancellationToken);
                var marketIndexTask = _twse.GetMarketIndicesAsync(cancellationToken);
                var monthlyAvgTask = _twse.GetMonthlyAveragesAsync(cancellationToken);
                var monthlyRevenueTask = _twse.GetMonthlyRevenuesAsync(cancellationToken);
                var incomeStatementTask = _twse.GetIncomeStatementsAsync(cancellationToken);
                var balanceSheetTask = _twse.GetBalanceSheetsAsync(cancellationToken);
                var dividendTask = _twse.GetDividendsAsync(cancellationToken);
                var valuationTask = _twse.GetValuationRatiosAsync(cancellationToken);
                var companyProfileTask = _twse.GetCompanyProfilesAsync(cancellationToken);

                await Task.WhenAll(
                    dailyTradingTask, marketIndexTask, monthlyAvgTask, monthlyRevenueTask,
                    incomeStatementTask, balanceSheetTask, dividendTask, valuationTask, companyProfileTask);

                var stockData = new StringBuilder();
                stockData.AppendLine($"=== Comprehensive Stock Analysis for {stockCode} ===");
                stockData.AppendLine();

                // Short-Term Analysis (1-3 months) - Technical & Chip Analysis
                stockData.AppendLine("=== SHORT-TERM ANALYSIS (1-3 months) ===");
                stockData.AppendLine("Data Sources: Technical indicators and market sentiment");
                stockData.AppendLine();
                AppendSection(stockData, "Daily Trading Data (Technical)", Describe(await dailyTradingTask, q => q.Code, stockCode));
                AppendSection(stockData, "Market Index & Institutional Activity (Chip)", DescribeMarketIndices(await marketIndexTask));

                // Medium-Term Analysis (3-12 months) - Technical & Fundamental
                stockData.AppendLine("=== MEDIUM-TERM ANALYSIS (3-12 months) ===");
                stockData.AppendLine("Data Sources: Monthly trends and fundamental performance");
                stockData.AppendLine();
                AppendSection(stockData, "Monthly Average Prices (Technical)", Describe(await monthlyAvgTask, r => r.Code, stockCode));
                AppendSection(stockData, "Monthly Revenue (Fundamental)", Describe(await monthlyRevenueTask, r => r.Code, stockCode));
                AppendSection(stockData, "Income Statement (Fundamental)", Describe(await incomeStatementTask, r => r.Code, stockCode));
                AppendSection(stockData, "Balance Sheet (Fundamental)", Describe(await balanceSheetTask, r => r.Code, stockCode));

                // Long-Term Analysis (1-3 years) - Valuation & Fundamentals
                stockData.AppendLine("=== LONG-TERM ANALYSIS (1-3 years) ===");
                stockData.AppendLine("Data Sources: Valuation metrics and dividend policy");
                stockData.AppendLine();
                AppendSection(stockData, "Dividend Distribution (Fundamental)", Describe(await dividendTask, r => r.Code, stockCode));
                AppendSection(stockData, "Valuation Ratios (P/E, P/B, Dividend Yield)", Describe(await valuationTask, r => r.Code, stockCode));

                // Company Profile for context
                stockData.AppendLine("=== COMPANY CONTEXT ===");
                AppendSection(stockData, "Company Profile", Describe(await companyProfileTask, p => p.Code, stockCode));

                var result = stockData.ToString();

                if (string.IsNullOrWhiteSpace(result) || result.Length < 200)
                {
                    _logger.LogWarning("Insufficient stock data retrieved from TWSE APIs for {stockCode}", stockCode);
                    return $"Limited data available for {stockCode}. Stock code provided for basic analysis.";
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve stock data from TWSE APIs for {stockCode}", stockCode);
                return $"Failed to retrieve stock data for {stockCode}. Error: {ex.Message}";
            }
        }

        private static void AppendSection(StringBuilder sb, string title, string content)
        {
            if (string.IsNullOrEmpty(content)) return;
            sb.AppendLine($"--- {title} ---");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        /// <summary>The stock's row of a dataset; empty when the dataset could not be read (its section
        /// is then left out).</summary>
        private static string Describe<T>(IReadOnlyList<T>? rows, Func<T, string> codeOf, string stockCode) where T : TwseRow
        {
            if (rows == null) return string.Empty;
            var row = rows.FirstOrDefault(r => codeOf(r) == stockCode);
            return row?.ToText() ?? $"No data found for stock code: {stockCode}";
        }

        /// <summary>The first index rows, whole, up to <see cref="MarketIndicesBudget"/> characters.</summary>
        private static string DescribeMarketIndices(IReadOnlyList<TwseMarketIndex>? indices)
        {
            if (indices == null) return string.Empty;

            var text = new StringBuilder();
            foreach (var index in indices)
            {
                var row = index.ToText();
                if (text.Length + row.Length > MarketIndicesBudget)
                {
                    text.AppendLine("...");
                    break;
                }
                text.Append(row);
            }
            return text.ToString();
        }
    }
}
