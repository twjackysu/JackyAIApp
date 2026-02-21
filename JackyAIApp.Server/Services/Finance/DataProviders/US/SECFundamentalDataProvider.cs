using JackyAIApp.Server.DTO.Finance;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance.DataProviders.US
{
    /// <summary>
    /// Fetches US stock fundamental data from SEC EDGAR XBRL API.
    /// Free, no API key. Requires User-Agent with company name and contact email.
    /// Rate limit: 10 requests/second.
    /// </summary>
    public class SECFundamentalDataProvider : IFundamentalDataProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SECFundamentalDataProvider> _logger;

        private const string TICKERS_URL = "https://www.sec.gov/files/company_tickers.json";
        private const string FACTS_URL = "https://data.sec.gov/api/xbrl/companyfacts/CIK{0}.json";
        private const string USER_AGENT = "JackyAIApp/1.0 jackysu@example.com";
        private const int CACHE_HOURS = 12;

        public SECFundamentalDataProvider(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            ILogger<SECFundamentalDataProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Compute derived ratios from SEC raw values using market price.
        /// SEC provides EPS, BVPS, annual dividend — not P/E, P/B, yield%.
        /// </summary>
        public void EnrichWithMarketPrice(FundamentalData data, decimal latestPrice)
        {
            if (latestPrice <= 0) return;

            // P/E = Price / Trailing EPS (annual)
            if (data.PERatio == null && data.TrailingEPS.HasValue && data.TrailingEPS.Value > 0)
                data.PERatio = Math.Round(latestPrice / data.TrailingEPS.Value, 2);

            // P/B = Price / Book Value Per Share
            // SEC provider stores BVPS in PBRatio field
            if (data.PBRatio.HasValue && data.PBRatio.Value > 0 && data.PBRatio.Value < latestPrice)
                data.PBRatio = Math.Round(latestPrice / data.PBRatio.Value, 2);

            // Dividend Yield % = (Annual Dividend / Price) * 100
            // SEC provider stores annualized dividend amount in DividendYield field
            if (data.DividendYield.HasValue && data.DividendYield.Value > 0 && data.DividendYield.Value < latestPrice)
                data.DividendYield = Math.Round((data.DividendYield.Value / latestPrice) * 100, 2);
        }

        public async Task<FundamentalData?> FetchAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var symbol = stockCode.ToUpperInvariant();
            var cacheKey = $"SEC_Fundamental_{symbol}";
            if (_cache.TryGetValue(cacheKey, out FundamentalData? cached))
                return cached;

            try
            {
                // 1. Resolve ticker to CIK
                var cik = await ResolveCIK(symbol, cancellationToken);
                if (cik == null)
                {
                    _logger.LogWarning("Could not find CIK for ticker {symbol}", symbol);
                    return null;
                }

                // 2. Fetch company facts
                var facts = await FetchCompanyFacts(cik, cancellationToken);
                if (facts == null) return null;

                // 3. Extract fundamental data
                var result = ExtractFundamentals(facts, symbol);

                if (result != null)
                    _cache.Set(cacheKey, result, TimeSpan.FromHours(CACHE_HOURS));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch SEC fundamental data for {symbol}", symbol);
                return null;
            }
        }

        private async Task<string?> ResolveCIK(string ticker, CancellationToken ct)
        {
            var tickers = await FetchTickersCached(ct);
            if (tickers == null) return null;

            foreach (var prop in tickers.RootElement.EnumerateObject())
            {
                var entry = prop.Value;
                if (entry.TryGetProperty("ticker", out var t) &&
                    t.GetString()?.Equals(ticker, StringComparison.OrdinalIgnoreCase) == true)
                {
                    var cikNum = entry.GetProperty("cik_str").GetInt64();
                    return cikNum.ToString("D10");
                }
            }

            return null;
        }

        private async Task<JsonDocument?> FetchTickersCached(CancellationToken ct)
        {
            if (_cache.TryGetValue("SEC_Tickers", out JsonDocument? cached))
                return cached;

            var client = CreateClient();
            var response = await client.GetAsync(TICKERS_URL, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            _cache.Set("SEC_Tickers", doc, TimeSpan.FromHours(24));
            return doc;
        }

        private async Task<JsonElement?> FetchCompanyFacts(string cik, CancellationToken ct)
        {
            var cacheKey = $"SEC_Facts_{cik}";
            if (_cache.TryGetValue(cacheKey, out JsonDocument? cached))
                return cached?.RootElement;

            var url = string.Format(FACTS_URL, cik);
            var client = CreateClient();
            var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            _cache.Set(cacheKey, doc, TimeSpan.FromHours(CACHE_HOURS));
            return doc.RootElement;
        }

        private FundamentalData? ExtractFundamentals(JsonElement? factsRoot, string symbol)
        {
            if (factsRoot == null) return null;
            var root = factsRoot.Value;

            if (!root.TryGetProperty("facts", out var facts)) return null;
            if (!facts.TryGetProperty("us-gaap", out var usgaap)) return null;

            var result = new FundamentalData();
            var hasData = false;

            // EPS — latest single quarter
            var latestQuarterlyEps = GetLatestQuarterlyEntry(usgaap, "EarningsPerShareDiluted", "USD/shares");
            if (latestQuarterlyEps.HasValue)
            {
                result.EPS = latestQuarterlyEps.Value.val;
                result.EpsDataPeriod = FormatEpsPeriod(latestQuarterlyEps.Value);
                hasData = true;
            }

            // Trailing EPS — TTM (sum of last 4 single-quarter EPS)
            var ttmResult = ComputeTTMEps(usgaap);
            if (ttmResult.HasValue)
            {
                result.TrailingEPS = ttmResult.Value.ttmEps;
                result.EpsDataPeriod = ttmResult.Value.period;
                hasData = true;
            }
            else
            {
                // Fallback: use latest 10-K annual EPS
                var annualEntry = GetLatestAnnualEntry(usgaap, "EarningsPerShareDiluted", "USD/shares");
                if (annualEntry.HasValue)
                {
                    result.TrailingEPS = annualEntry.Value.val;
                    result.EpsDataPeriod ??= FormatEpsPeriod(annualEntry.Value);
                    hasData = true;
                }
            }

            // Revenue — latest quarterly + YoY computation
            var revenueResult = ComputeQuarterlyRevenueWithYoY(usgaap);
            if (revenueResult.HasValue)
            {
                result.MonthlyRevenue = revenueResult.Value.revenue / 1_000_000; // Store in millions (USD)
                result.RevenueYoY = revenueResult.Value.yoy;
                result.RevenueMonth = revenueResult.Value.period;
                hasData = true;
            }

            // Net Income
            var netIncome = GetLatestQuarterlyValue(usgaap, "NetIncomeLoss", "USD");
            if (netIncome.HasValue)
            {
                result.NetIncome = netIncome.Value / 1000;
                hasData = true;
            }

            // Operating Income
            var opIncome = GetLatestQuarterlyValue(usgaap, "OperatingIncomeLoss", "USD");
            if (opIncome.HasValue)
            {
                result.OperatingIncome = opIncome.Value / 1000;
                hasData = true;
            }

            // Dividend per share
            var dividend = GetLatestQuarterlyValue(usgaap, "CommonStockDividendsPerShareDeclared", "USD/shares");

            // Shares outstanding
            var shares = GetLatestValue(usgaap, "CommonStockSharesOutstanding", "shares");

            // Stockholders' equity for book value
            var equity = GetLatestValue(usgaap, "StockholdersEquity", "USD");

            // Compute P/B from equity and shares (P/E needs market price, done at higher level)
            if (equity.HasValue && shares.HasValue && shares.Value > 0)
            {
                var bookValuePerShare = equity.Value / shares.Value;
                // P/B will be computed by the indicator calculator using market price
                // Store book value per share in a field — we'll use PBRatio field for BVPS temporarily
                // Actually, let's compute it if we can get price from the caller
                result.PBRatio = bookValuePerShare; // Store BVPS; indicator will compute actual P/B
                hasData = true;
            }

            // Dividend yield needs market price too — store annual dividend
            if (dividend.HasValue)
            {
                // Store annual dividend in DividendYield field temporarily
                // The actual yield = (annualDividend / price) * 100, computed at indicator level
                result.DividendYield = dividend.Value * 4; // Annualize quarterly dividend
                hasData = true;
            }

            // Fiscal quarter info
            var latestFilingEnd = GetLatestFilingPeriod(usgaap, "EarningsPerShareDiluted", "USD/shares");
            if (latestFilingEnd != null)
            {
                result.FiscalYearQuarter = latestFilingEnd;
                hasData = true;
            }

            return hasData ? result : null;
        }

        /// <summary>Get latest quarterly value (single-quarter, not cumulative)</summary>
        private decimal? GetLatestQuarterlyValue(JsonElement usgaap, string concept, string unitType)
            => GetLatestQuarterlyEntry(usgaap, concept, unitType)?.val;

        /// <summary>Get latest annual (10-K or full-year cumulative) value</summary>
        private decimal? GetLatestAnnualValue(JsonElement usgaap, string concept, string unitType)
            => GetLatestAnnualEntry(usgaap, concept, unitType)?.val;

        /// <summary>Get latest value regardless of form type</summary>
        private decimal? GetLatestValue(JsonElement usgaap, string concept, string unitType)
        {
            if (!usgaap.TryGetProperty(concept, out var conceptEl)) return null;
            if (!conceptEl.TryGetProperty("units", out var units)) return null;
            if (!units.TryGetProperty(unitType, out var entries)) return null;

            decimal? latest = null;
            string latestEnd = "";

            foreach (var entry in entries.EnumerateArray())
            {
                var form = entry.TryGetProperty("form", out var f) ? f.GetString() : "";
                if (form != "10-Q" && form != "10-K") continue;

                var endDate = entry.TryGetProperty("end", out var e) ? e.GetString() ?? "" : "";
                var val = entry.TryGetProperty("val", out var v) ? GetDecimal(v) : null;

                if (val.HasValue && string.Compare(endDate, latestEnd) > 0)
                {
                    latest = val;
                    latestEnd = endDate;
                }
            }

            return latest;
        }

        /// <summary>
        /// Compute TTM (Trailing Twelve Months) EPS by summing the last 4 single-quarter EPS values.
        /// Deduplicates by period (start~end) to handle amended filings.
        /// </summary>
        private (decimal ttmEps, string period)? ComputeTTMEps(JsonElement usgaap)
        {
            if (!usgaap.TryGetProperty("EarningsPerShareDiluted", out var conceptEl)) return null;
            if (!conceptEl.TryGetProperty("units", out var units)) return null;
            if (!units.TryGetProperty("USD/shares", out var entries)) return null;

            var seen = new HashSet<string>();
            var quarterlyEntries = new List<FilingEntry>();

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("form", out var formEl)) continue;
                var form = formEl.GetString();
                if (form != "10-Q" && form != "10-K") continue;

                var startDate = entry.TryGetProperty("start", out var s) ? s.GetString() ?? "" : "";
                var endDate = entry.TryGetProperty("end", out var e) ? e.GetString() ?? "" : "";
                var val = entry.TryGetProperty("val", out var v) ? GetDecimal(v) : null;

                if (!val.HasValue || string.IsNullOrEmpty(startDate) || string.IsNullOrEmpty(endDate)) continue;

                // Only single-quarter periods (60-120 days)
                if (DateTime.TryParse(startDate, out var sd) && DateTime.TryParse(endDate, out var ed))
                {
                    var days = (ed - sd).TotalDays;
                    if (days < 60 || days > 120) continue;
                }
                else continue;

                // Deduplicate by period (amended filings have same period)
                var periodKey = $"{startDate}~{endDate}";
                if (!seen.Add(periodKey)) continue;

                quarterlyEntries.Add(new FilingEntry(startDate, endDate, form ?? "", val.Value));
            }

            if (quarterlyEntries.Count < 4) return null;

            // Sort by end date, take last 4
            quarterlyEntries.Sort((a, b) => string.Compare(a.end, b.end));
            var last4 = quarterlyEntries.Skip(quarterlyEntries.Count - 4).ToList();

            var ttm = last4.Sum(q => q.val);
            var period = $"TTM {last4[0].start} ~ {last4[3].end}";

            return (Math.Round(ttm, 2), period);
        }

        /// <summary>
        /// Get latest quarterly revenue with YoY growth computed from same quarter last year.
        /// </summary>
        private (decimal revenue, decimal? yoy, string period)? ComputeQuarterlyRevenueWithYoY(JsonElement usgaap)
        {
            // Try both common revenue concept names
            var concept = "RevenueFromContractWithCustomerExcludingAssessedTax";
            if (!usgaap.TryGetProperty(concept, out _))
            {
                concept = "Revenues";
                if (!usgaap.TryGetProperty(concept, out _)) return null;
            }

            if (!usgaap.TryGetProperty(concept, out var conceptEl)) return null;
            if (!conceptEl.TryGetProperty("units", out var units)) return null;
            if (!units.TryGetProperty("USD", out var entries)) return null;

            var seen = new HashSet<string>();
            var quarterlyEntries = new List<FilingEntry>();

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("form", out var formEl)) continue;
                var form = formEl.GetString();
                if (form != "10-Q" && form != "10-K") continue;

                var startDate = entry.TryGetProperty("start", out var s) ? s.GetString() ?? "" : "";
                var endDate = entry.TryGetProperty("end", out var e) ? e.GetString() ?? "" : "";
                var val = entry.TryGetProperty("val", out var v) ? GetDecimal(v) : null;

                if (!val.HasValue || string.IsNullOrEmpty(startDate) || string.IsNullOrEmpty(endDate)) continue;

                if (DateTime.TryParse(startDate, out var sd) && DateTime.TryParse(endDate, out var ed))
                {
                    var days = (ed - sd).TotalDays;
                    if (days < 60 || days > 120) continue;
                }
                else continue;

                var periodKey = $"{startDate}~{endDate}";
                if (!seen.Add(periodKey)) continue;

                quarterlyEntries.Add(new FilingEntry(startDate, endDate, form ?? "", val.Value));
            }

            if (quarterlyEntries.Count == 0) return null;

            quarterlyEntries.Sort((a, b) => string.Compare(a.end, b.end));
            var latest = quarterlyEntries[^1];

            // Find same quarter last year (~4 quarters back, match by similar month)
            decimal? yoy = null;
            if (DateTime.TryParse(latest.end, out var latestEnd))
            {
                var targetEnd = latestEnd.AddYears(-1);
                // Find closest quarter to target (within 45 days)
                var prevYear = quarterlyEntries
                    .Where(q => q.end != latest.end && DateTime.TryParse(q.end, out var qEnd)
                        && Math.Abs((qEnd - targetEnd).TotalDays) < 45)
                    .OrderBy(q => DateTime.TryParse(q.end, out var qEnd) ? Math.Abs((qEnd - targetEnd).TotalDays) : 999)
                    .FirstOrDefault();

                if (prevYear != default && prevYear.val > 0)
                    yoy = Math.Round(((latest.val - prevYear.val) / prevYear.val) * 100, 1);
            }

            var period = $"{latest.start} ~ {latest.end}";
            return (latest.val, yoy, period);
        }

        private record struct FilingEntry(string start, string end, string form, decimal val);

        /// <summary>Get latest quarterly filing entry with full period info</summary>
        private FilingEntry? GetLatestQuarterlyEntry(JsonElement usgaap, string concept, string unitType)
        {
            if (!usgaap.TryGetProperty(concept, out var conceptEl)) return null;
            if (!conceptEl.TryGetProperty("units", out var units)) return null;
            if (!units.TryGetProperty(unitType, out var entries)) return null;

            var filings = new List<FilingEntry>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("form", out var formEl)) continue;
                var form = formEl.GetString();
                if (form != "10-Q" && form != "10-K") continue;

                var endDate = entry.TryGetProperty("end", out var e) ? e.GetString() ?? "" : "";
                var startDate = entry.TryGetProperty("start", out var s) ? s.GetString() ?? "" : "";
                var val = entry.TryGetProperty("val", out var v) ? GetDecimal(v) : null;

                if (val.HasValue && !string.IsNullOrEmpty(endDate))
                    filings.Add(new FilingEntry(startDate, endDate, form ?? "", val.Value));
            }

            if (filings.Count == 0) return null;

            // Prefer single-quarter entries (~90 days)
            var quarterly = filings
                .Where(f => !string.IsNullOrEmpty(f.start))
                .Where(f =>
                {
                    if (DateTime.TryParse(f.start, out var s) && DateTime.TryParse(f.end, out var e))
                        return (e - s).TotalDays < 120;
                    return false;
                })
                .OrderByDescending(f => f.end)
                .FirstOrDefault();

            return quarterly != default ? quarterly : filings.OrderByDescending(f => f.end).First();
        }

        /// <summary>Get latest annual (10-K) filing entry with full period info</summary>
        private FilingEntry? GetLatestAnnualEntry(JsonElement usgaap, string concept, string unitType)
        {
            if (!usgaap.TryGetProperty(concept, out var conceptEl)) return null;
            if (!conceptEl.TryGetProperty("units", out var units)) return null;
            if (!units.TryGetProperty(unitType, out var entries)) return null;

            FilingEntry? latest = null;

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("form", out var formEl)) continue;
                if (formEl.GetString() != "10-K") continue;

                var endDate = entry.TryGetProperty("end", out var e) ? e.GetString() ?? "" : "";
                var startDate = entry.TryGetProperty("start", out var s) ? s.GetString() ?? "" : "";
                var val = entry.TryGetProperty("val", out var v) ? GetDecimal(v) : null;

                if (val.HasValue && (latest == null || string.Compare(endDate, latest.Value.end) > 0))
                    latest = new FilingEntry(startDate, endDate, "10-K", val.Value);
            }

            return latest;
        }

        /// <summary>Format filing entry into human-readable EPS period description</summary>
        private static string FormatEpsPeriod(FilingEntry entry)
        {
            var isAnnual = entry.form == "10-K";
            if (isAnnual)
                return $"FY ending {entry.end}";

            // Quarterly: determine Q number from end date month
            if (DateTime.TryParse(entry.end, out var endDate))
            {
                var periodLabel = $"Q ending {entry.end}";
                if (DateTime.TryParse(entry.start, out var startDate))
                    periodLabel = $"{entry.start} ~ {entry.end}";
                return periodLabel;
            }

            return $"{entry.form} ending {entry.end}";
        }

        private string? GetLatestFilingPeriod(JsonElement usgaap, string concept, string unitType)
        {
            if (!usgaap.TryGetProperty(concept, out var conceptEl)) return null;
            if (!conceptEl.TryGetProperty("units", out var units)) return null;
            if (!units.TryGetProperty(unitType, out var entries)) return null;

            string latest = "";
            foreach (var entry in entries.EnumerateArray())
            {
                var form = entry.TryGetProperty("form", out var f) ? f.GetString() : "";
                if (form != "10-Q" && form != "10-K") continue;
                var endDate = entry.TryGetProperty("end", out var e) ? e.GetString() ?? "" : "";
                if (string.Compare(endDate, latest) > 0) latest = endDate;
            }

            return string.IsNullOrEmpty(latest) ? null : latest;
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient();
            // Must use TryAddWithoutValidation — .NET's strict header parser
            // rejects User-Agent values containing '@' (email format).
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", USER_AGENT);
            return client;
        }

        private static decimal? GetDecimal(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number)
                return el.TryGetDecimal(out var d) ? d : (decimal?)el.GetDouble();
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString()?.Trim();
                return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
            }
            return null;
        }
    }
}
