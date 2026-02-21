using HtmlAgilityPack;
using JackyAIApp.Server.DTO.Finance;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;

namespace JackyAIApp.Server.Services.Finance.DataProviders.US
{
    /// <summary>
    /// Fetches insider trading data from SEC Form 4 filings.
    /// Uses SEC EDGAR ownership table (HTML parsing).
    /// Free, no API key. Requires User-Agent with company name and contact email.
    /// Rate limit: 10 requests/second.
    /// </summary>
    public class SECInsiderTradingProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SECInsiderTradingProvider> _logger;

        private const string OWNERSHIP_URL = "https://www.sec.gov/cgi-bin/own-disp?action=getissuer&CIK={0}&type=&dateb=&owner=include&start=0";
        private const string USER_AGENT = "JackyAIApp/1.0 jacky19918@gmail.com";
        private const int CACHE_HOURS = 4;

        public SECInsiderTradingProvider(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            ILogger<SECInsiderTradingProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _logger = logger;
        }

        public async Task<InsiderTradingSummary?> FetchAsync(string cik, string symbol, CancellationToken ct = default)
        {
            var cacheKey = $"SEC_Insider_{cik}";
            if (_cache.TryGetValue(cacheKey, out InsiderTradingSummary? cached))
                return cached;

            try
            {
                var html = await FetchOwnershipTable(cik, ct);
                if (string.IsNullOrEmpty(html)) return null;

                var transactions = ParseOwnershipTable(html);
                if (transactions.Count == 0) return null;

                // Filter last 90 days
                var cutoffDate = DateTime.UtcNow.AddDays(-90);
                var recent = transactions
                    .Where(t => DateTime.TryParse(t.TransactionDate, out var d) && d >= cutoffDate)
                    .OrderByDescending(t => t.TransactionDate)
                    .ToList();

                var summary = new InsiderTradingSummary
                {
                    StockCode = symbol,
                    RecentTransactions = recent.Take(50).ToList(), // Keep top 50
                    FetchedAt = DateTime.UtcNow
                };

                // Compute aggregates (only P=Purchase, S=Sale with known prices)
                var purchases = recent.Where(t => t.TransactionCode == "P" && t.TransactionValue.HasValue).ToList();
                var sales = recent.Where(t => t.TransactionCode == "S" && t.TransactionValue.HasValue).ToList();

                summary.PurchaseCount = purchases.Count;
                summary.SaleCount = sales.Count;
                summary.TotalPurchaseValue = purchases.Sum(t => t.TransactionValue ?? 0);
                summary.TotalSaleValue = sales.Sum(t => t.TransactionValue ?? 0);
                summary.NetBuyingValue = summary.TotalPurchaseValue - summary.TotalSaleValue;
                summary.NetBuyingShares = purchases.Sum(t => t.Shares) - sales.Sum(t => t.Shares);

                _cache.Set(cacheKey, summary, TimeSpan.FromHours(CACHE_HOURS));
                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch insider trading for CIK {cik}", cik);
                return null;
            }
        }

        private async Task<string?> FetchOwnershipTable(string cik, CancellationToken ct)
        {
            var url = string.Format(OWNERSHIP_URL, cik);
            var client = CreateClient();
            var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }

        private List<InsiderTransaction> ParseOwnershipTable(string html)
        {
            var transactions = new List<InsiderTransaction>();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Find transaction table
            var table = doc.DocumentNode.SelectSingleNode("//table[@id='transaction-report']");
            if (table == null) return transactions;

            var rows = table.SelectNodes(".//tr[position()>1]"); // Skip header
            if (rows == null) return transactions;

            foreach (var row in rows)
            {
                try
                {
                    var cells = row.SelectNodes(".//td")?.Select(c => c.InnerText.Trim()).ToArray();
                    if (cells == null || cells.Length < 7) continue;

                    // Columns: Owner, Relationship, TxDate, TxCode, Shares, PricePerShare, SharesOwnedAfter, [FilingDate, AccessionNo]
                    var tx = new InsiderTransaction
                    {
                        OwnerName = cells.ElementAtOrDefault(0) ?? "",
                        Relationship = cells.ElementAtOrDefault(1) ?? "",
                        TransactionDate = NormalizeDate(cells.ElementAtOrDefault(2) ?? ""),
                        TransactionCode = cells.ElementAtOrDefault(3) ?? "",
                        Shares = ParseDecimal(cells.ElementAtOrDefault(4)) ?? 0,
                        PricePerShare = ParseDecimal(cells.ElementAtOrDefault(5)),
                        SharesOwnedAfter = ParseDecimal(cells.ElementAtOrDefault(6)),
                        FilingDate = NormalizeDate(cells.ElementAtOrDefault(7) ?? ""),
                        AccessionNumber = cells.ElementAtOrDefault(8) ?? ""
                    };

                    if (tx.PricePerShare.HasValue && tx.PricePerShare.Value > 0)
                        tx.TransactionValue = tx.Shares * tx.PricePerShare.Value;

                    transactions.Add(tx);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse insider transaction row");
                }
            }

            return transactions;
        }

        private static decimal? ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Replace(",", "").Replace("$", "").Trim();
            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        private static string NormalizeDate(string date)
        {
            // SEC format: MM/DD/YYYY or YYYY-MM-DD
            if (DateTime.TryParseExact(date, new[] { "MM/dd/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d.ToString("yyyy-MM-dd");
            return date;
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", USER_AGENT);
            return client;
        }
    }
}
