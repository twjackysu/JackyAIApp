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

                // Compute aggregates (P=Purchase, S=Sale)
                var purchases = recent.Where(t => t.TransactionCode == "P").ToList();
                var sales = recent.Where(t => t.TransactionCode == "S").ToList();

                summary.PurchaseCount = purchases.Count;
                summary.SaleCount = sales.Count;

                // Value-based metrics (only if prices available)
                var purchasesWithValue = purchases.Where(t => t.TransactionValue.HasValue).ToList();
                var salesWithValue = sales.Where(t => t.TransactionValue.HasValue).ToList();
                summary.TotalPurchaseValue = purchasesWithValue.Sum(t => t.TransactionValue ?? 0);
                summary.TotalSaleValue = salesWithValue.Sum(t => t.TransactionValue ?? 0);
                summary.NetBuyingValue = purchasesWithValue.Any() || salesWithValue.Any()
                    ? summary.TotalPurchaseValue - summary.TotalSaleValue
                    : null;

                // Share-based metrics (always available)
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
                    if (cells == null || cells.Length < 9) continue;

                    // Actual columns (from SEC insider table):
                    // 0: Acquisition/Disposition (A/D)
                    // 1: Transaction Date
                    // 2: Deemed Execution Date (usually empty)
                    // 3: Reporting Owner (name)
                    // 4: Form (link to filing)
                    // 5: Transaction Type (code, e.g., "M-Exempt", "S-Sale", "P-Purchase")
                    // 6: Direct/Indirect
                    // 7: Number of Securities (shares)
                    // 8: Number Owned After
                    // 9: Line Number
                    // 10: Owner CIK
                    // 11: Security Name

                    var ownerName = cells.ElementAtOrDefault(3) ?? "";
                    var txDate = cells.ElementAtOrDefault(1) ?? "";
                    var txTypeRaw = cells.ElementAtOrDefault(5) ?? "";
                    var sharesStr = cells.ElementAtOrDefault(7) ?? "";
                    var ownedAfterStr = cells.ElementAtOrDefault(8) ?? "";

                    // Extract simple transaction code (P/S/M/A) from full type string
                    var txCode = ExtractTransactionCode(txTypeRaw);
                    if (string.IsNullOrEmpty(txCode)) continue;

                    var shares = ParseDecimal(sharesStr) ?? 0;
                    if (shares <= 0) continue;

                    var tx = new InsiderTransaction
                    {
                        OwnerName = ownerName,
                        Relationship = "", // Not available in this table format
                        TransactionDate = NormalizeDate(txDate),
                        TransactionCode = txCode,
                        Shares = shares,
                        PricePerShare = null, // Price not in this table
                        SharesOwnedAfter = ParseDecimal(ownedAfterStr),
                        TransactionValue = null, // Can't compute without price
                        FilingDate = txDate, // Use tx date as proxy
                        AccessionNumber = ""
                    };

                    transactions.Add(tx);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse insider transaction row");
                }
            }

            return transactions;
        }

        /// <summary>Extract transaction code from SEC type string (e.g., "M-Exempt" → "M", "S-Sale" → "S")</summary>
        private static string ExtractTransactionCode(string typeString)
        {
            if (string.IsNullOrWhiteSpace(typeString)) return "";
            var parts = typeString.Split('-', ' ');
            var code = parts[0].Trim().ToUpperInvariant();
            // P=Purchase, S=Sale, M=Exercise, A=Award, D=Disposition
            return code.Length == 1 && "PSMAD".Contains(code) ? code : "";
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
