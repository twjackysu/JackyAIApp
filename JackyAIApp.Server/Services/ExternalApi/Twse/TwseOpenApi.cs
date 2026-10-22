using System.Collections.Concurrent;
using JackyAIApp.Server.Services.Market;
using System.Globalization;
using System.Text;
using System.Text.Json;
using JackyAIApp.Server.Common;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.ExternalApi.Twse
{
    /// <summary>
    /// A row of a TWSE OpenAPI dataset. <see cref="Fields"/> is every field as TWSE sent it, in its
    /// order — what an AI prompt quotes; the typed properties of each row type are the ones code reads.
    /// Field names are trimmed: TWSE pads a few of them ("主旨 " in t187ap04_L).
    /// </summary>
    public abstract record TwseRow(IReadOnlyList<KeyValuePair<string, string>> Fields)
    {
        /// <summary>A field's value as TWSE sent it; null when the row has no such field.</summary>
        public string? Value(string field)
        {
            foreach (var (name, value) in Fields)
                if (name == field) return value;
            return null;
        }

        /// <summary>Every field as a "field: value" line, in TWSE's order — how a prompt quotes a row.</summary>
        public string ToText()
        {
            var text = new StringBuilder();
            foreach (var (name, value) in Fields)
                text.AppendLine($"{name}: {value}");
            return text.ToString();
        }
    }

    /// <summary>One stock's row of a dataset keyed by stock code; its other fields are read by name
    /// (<see cref="TwseRow.Value"/>) or quoted whole. Some datasets have several rows per stock (one
    /// per director, per major shareholder).</summary>
    public sealed record TwseStockRow(string Code, IReadOnlyList<KeyValuePair<string, string>> Fields)
        : TwseRow(Fields);

    /// <summary>One row of a dataset not keyed by a stock: a trading day of the market (FMTQIK), an
    /// announcement (t187ap04_L), a holiday (holidaySchedule).</summary>
    public sealed record TwseDataRow(IReadOnlyList<KeyValuePair<string, string>> Fields) : TwseRow(Fields);

    /// <summary>One of the market's indices on the latest trading day (MI_INDEX).</summary>
    public sealed record TwseMarketIndex(string Name, IReadOnlyList<KeyValuePair<string, string>> Fields)
        : TwseRow(Fields);

    /// <summary>One 上市 security's latest trading day (STOCK_DAY_ALL).</summary>
    public sealed record TwseDailyQuote(string Code, string Name, decimal? ClosingPrice, IReadOnlyList<KeyValuePair<string, string>> Fields)
        : TwseRow(Fields);

    /// <summary>One 上市 company's basic data (t187ap03_L).</summary>
    public sealed record TwseCompanyProfile(string Code, string Name, IReadOnlyList<KeyValuePair<string, string>> Fields)
        : TwseRow(Fields);

    /// <summary>One 上市 company's latest reported month of revenue (t187ap05_L). Revenue is in
    /// thousands of TWD, the changes in percent.</summary>
    public sealed record TwseMonthlyRevenue(
        string Code, string? DataMonth, decimal? Revenue, decimal? RevenueYoY, decimal? RevenueMoM,
        IReadOnlyList<KeyValuePair<string, string>> Fields) : TwseRow(Fields);

    /// <summary>
    /// TWSE's OpenAPI (openapi.twse.com.tw), the one place that reads it: each dataset is a
    /// market-wide array, downloaded once and shared by every caller until it expires (one fetch for
    /// a burst of callers, cached per Taipei day), and parsed once into typed rows. A dataset that
    /// could not be read — a failed request, something other than a JSON array, no rows with a code —
    /// is null and not cached, so the next caller tries again.
    /// </summary>
    public interface ITwseOpenApi
    {
        /// <summary>Every 上市 security that traded on the latest trading day: stocks, ETFs, ETNs,
        /// preferred shares, DRs. No warrants.</summary>
        Task<IReadOnlyList<TwseDailyQuote>?> GetDailyQuotesAsync(CancellationToken ct = default);

        Task<IReadOnlyList<TwseCompanyProfile>?> GetCompanyProfilesAsync(CancellationToken ct = default);

        Task<IReadOnlyList<TwseMonthlyRevenue>?> GetMonthlyRevenuesAsync(CancellationToken ct = default);

        /// <summary>Each security's latest close and this month's average price (STOCK_DAY_AVG_ALL).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetMonthlyAveragesAsync(CancellationToken ct = default);

        /// <summary>P/E, P/B and dividend yield by stock (BWIBBU_ALL).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetValuationRatiosAsync(CancellationToken ct = default);

        /// <summary>Dividend distributions decided by the board or shareholders (t187ap45_L).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetDividendsAsync(CancellationToken ct = default);

        /// <summary>The latest consolidated income statements of general-industry companies (t187ap06_L_ci).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetIncomeStatementsAsync(CancellationToken ct = default);

        /// <summary>The latest consolidated balance sheets of general-industry companies (t187ap07_L_ci).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetBalanceSheetsAsync(CancellationToken ct = default);

        /// <summary>Every market index's close on the latest trading day (MI_INDEX).</summary>
        Task<IReadOnlyList<TwseMarketIndex>?> GetMarketIndicesAsync(CancellationToken ct = default);

        /// <summary>The market's totals for each trading day of the month so far: TAIEX, its change,
        /// volume, value, transactions (FMTQIK).</summary>
        Task<IReadOnlyList<TwseDataRow>?> GetMarketDaysAsync(CancellationToken ct = default);

        /// <summary>Margin trading and short selling by stock on the latest trading day (MI_MARGN).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetMarginTradingAsync(CancellationToken ct = default);

        /// <summary>The 20 stocks foreign investors hold the largest share of (MI_QFIIS_sort_20).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetForeignHoldingsTop20Async(CancellationToken ct = default);

        /// <summary>Shares available to borrow by stock (SBL TWT96U), keyed by its TWSE code.</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetSecuritiesLendingAsync(CancellationToken ct = default);

        /// <summary>Directors' and supervisors' holdings and pledges, one row each (t187ap11_L).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetDirectorHoldingsAsync(CancellationToken ct = default);

        /// <summary>Shareholders holding more than 10%, one row each (t187ap02_L).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetMajorShareholdersAsync(CancellationToken ct = default);

        /// <summary>Stocks with day trading suspended (TWTB4U).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetDayTradingSuspensionsAsync(CancellationToken ct = default);

        /// <summary>P/E, P/B and dividend yield by stock, with their fiscal quarter (BWIBBU_d).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetDailyValuationsAsync(CancellationToken ct = default);

        /// <summary>The latest reported quarter's EPS and income by company (t187ap14_L).</summary>
        Task<IReadOnlyList<TwseStockRow>?> GetQuarterlyEpsAsync(CancellationToken ct = default);

        /// <summary>Today's material announcements (重大訊息, t187ap04_L). On a day without any this is
        /// null like a failed read, and asked for again next time.</summary>
        Task<IReadOnlyList<TwseDataRow>?> GetMaterialInfoAsync(CancellationToken ct = default);

        /// <summary>The market holiday schedule of the year (holidaySchedule).</summary>
        Task<IReadOnlyList<TwseDataRow>?> GetHolidayScheduleAsync(CancellationToken ct = default);
    }

    public class TwseOpenApi(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<TwseOpenApi> logger,
        TimeProvider time) : ITwseOpenApi
    {
        /// <summary>Named client: gzip, and a timeout sized for a slow Taiwan-to-US path rather than for
        /// a user waiting on a request (see ExternalApiServiceExtensions). Uncompressed, STOCK_DAY_ALL
        /// alone took ~23s from East US.</summary>
        internal const string HttpClientName = "TwseOpenApi";

        internal const string BaseUrl = "https://openapi.twse.com.tw/v1/";
        internal const string DailyQuotesUrl = BaseUrl + "exchangeReport/STOCK_DAY_ALL";
        internal const string CompanyProfilesUrl = BaseUrl + "opendata/t187ap03_L";
        internal const string MonthlyRevenuesUrl = BaseUrl + "opendata/t187ap05_L";
        internal const string MonthlyAveragesUrl = BaseUrl + "exchangeReport/STOCK_DAY_AVG_ALL";
        internal const string ValuationRatiosUrl = BaseUrl + "exchangeReport/BWIBBU_ALL";
        internal const string DividendsUrl = BaseUrl + "opendata/t187ap45_L";
        internal const string IncomeStatementsUrl = BaseUrl + "opendata/t187ap06_L_ci";
        internal const string BalanceSheetsUrl = BaseUrl + "opendata/t187ap07_L_ci";
        // Takes no query: the response=json&date=&type= the old www.twse.com.tw API wanted change nothing here.
        internal const string MarketIndicesUrl = BaseUrl + "exchangeReport/MI_INDEX";
        internal const string MarketDaysUrl = BaseUrl + "exchangeReport/FMTQIK";
        internal const string MarginTradingUrl = BaseUrl + "exchangeReport/MI_MARGN";
        internal const string ForeignHoldingsTop20Url = BaseUrl + "fund/MI_QFIIS_sort_20";
        internal const string SecuritiesLendingUrl = BaseUrl + "SBL/TWT96U";
        internal const string DirectorHoldingsUrl = BaseUrl + "opendata/t187ap11_L";
        internal const string MajorShareholdersUrl = BaseUrl + "opendata/t187ap02_L";
        internal const string DayTradingSuspensionsUrl = BaseUrl + "exchangeReport/TWTB4U";
        internal const string DailyValuationsUrl = BaseUrl + "exchangeReport/BWIBBU_d";
        internal const string QuarterlyEpsUrl = BaseUrl + "opendata/t187ap14_L";
        internal const string MaterialInfoUrl = BaseUrl + "opendata/t187ap04_L";
        internal const string HolidayScheduleUrl = BaseUrl + "holidaySchedule/holidaySchedule";

        // How long a dataset is served before it is downloaded again (and never past the Taipei day).
        // The market's own totals change once a day, after the close, and the market pages read them
        // soon after: two hours. Per-stock data and filings: four. The calendar and the day's
        // announcements: twelve. Company data: a day.
        private static readonly TimeSpan MarketTotals = TimeSpan.FromHours(2);
        private static readonly TimeSpan PerStock = TimeSpan.FromHours(4);
        private static readonly TimeSpan HalfADay = TimeSpan.FromHours(12);

        private sealed record Dataset<T>(string Url, TimeSpan Ttl, Func<JsonElement, T?> Read) where T : class;

        private static readonly Dataset<TwseDailyQuote> DailyQuotes = new(DailyQuotesUrl, PerStock,
            row => Text(row, "Code") is { Length: > 0 } code
                ? new TwseDailyQuote(code, Text(row, "Name"), Number(row, "ClosingPrice"), FieldsOf(row))
                : null);

        // Company data rarely changes: a day is fresh enough.
        private static readonly Dataset<TwseCompanyProfile> CompanyProfiles = new(CompanyProfilesUrl, TimeSpan.FromHours(24),
            row => Text(row, "公司代號") is { Length: > 0 } code
                ? new TwseCompanyProfile(code, Text(row, "公司名稱"), FieldsOf(row))
                : null);

        private static readonly Dataset<TwseMonthlyRevenue> MonthlyRevenues = new(MonthlyRevenuesUrl, PerStock,
            row => Text(row, "公司代號") is { Length: > 0 } code
                ? new TwseMonthlyRevenue(code,
                    Text(row, "資料年月") is { Length: > 0 } month ? month : null,
                    Number(row, "營業收入-當月營收"),
                    Number(row, "營業收入-去年同月增減(%)"),
                    Number(row, "營業收入-上月比較增減(%)"),
                    FieldsOf(row))
                : null);

        // ~34,000 rows, nearly all of them warrants, which nothing here quotes.
        private static readonly Dataset<TwseStockRow> MonthlyAverages = StockRows(MonthlyAveragesUrl, "Code", dropWarrants: true);
        private static readonly Dataset<TwseStockRow> ValuationRatios = StockRows(ValuationRatiosUrl, "Code");
        private static readonly Dataset<TwseStockRow> Dividends = StockRows(DividendsUrl, "公司代號");
        private static readonly Dataset<TwseStockRow> IncomeStatements = StockRows(IncomeStatementsUrl, "公司代號");
        private static readonly Dataset<TwseStockRow> BalanceSheets = StockRows(BalanceSheetsUrl, "公司代號");

        private static readonly Dataset<TwseMarketIndex> MarketIndices = new(MarketIndicesUrl, MarketTotals,
            row => Text(row, "指數") is { Length: > 0 } name ? new TwseMarketIndex(name, FieldsOf(row)) : null);

        private static readonly Dataset<TwseDataRow> MarketDays = DataRows(MarketDaysUrl, MarketTotals);
        private static readonly Dataset<TwseStockRow> MarginTrading = StockRows(MarginTradingUrl, "股票代號", MarketTotals);
        private static readonly Dataset<TwseStockRow> ForeignHoldingsTop20 = StockRows(ForeignHoldingsTop20Url, "Code");
        private static readonly Dataset<TwseStockRow> SecuritiesLending = StockRows(SecuritiesLendingUrl, "TWSECode");
        private static readonly Dataset<TwseStockRow> DirectorHoldings = StockRows(DirectorHoldingsUrl, "公司代號");
        private static readonly Dataset<TwseStockRow> MajorShareholders = StockRows(MajorShareholdersUrl, "公司代號");
        private static readonly Dataset<TwseStockRow> DayTradingSuspensions = StockRows(DayTradingSuspensionsUrl, "Code");
        private static readonly Dataset<TwseStockRow> DailyValuations = StockRows(DailyValuationsUrl, "Code");
        private static readonly Dataset<TwseStockRow> QuarterlyEps = StockRows(QuarterlyEpsUrl, "公司代號");
        private static readonly Dataset<TwseDataRow> MaterialInfo = DataRows(MaterialInfoUrl, HalfADay);
        // Not cached: the trading calendar holds the schedule itself, and reloads it on its own schedule.
        private static readonly Dataset<TwseDataRow> HolidaySchedule = DataRows(HolidayScheduleUrl, TimeSpan.Zero);

        private static Dataset<TwseStockRow> StockRows(string url, string codeField, TimeSpan? ttl = null, bool dropWarrants = false) =>
            new(url, ttl ?? PerStock,
                row => Text(row, codeField) is { Length: > 0 } code && !(dropWarrants && TwStockCode.IsWarrant(code))
                    ? new TwseStockRow(code, FieldsOf(row))
                    : null);

        private static Dataset<TwseDataRow> DataRows(string url, TimeSpan ttl) => new(url, ttl, row => new TwseDataRow(FieldsOf(row)));

        public Task<IReadOnlyList<TwseDailyQuote>?> GetDailyQuotesAsync(CancellationToken ct = default) => GetAsync(DailyQuotes, ct);

        public Task<IReadOnlyList<TwseCompanyProfile>?> GetCompanyProfilesAsync(CancellationToken ct = default) => GetAsync(CompanyProfiles, ct);

        public Task<IReadOnlyList<TwseMonthlyRevenue>?> GetMonthlyRevenuesAsync(CancellationToken ct = default) => GetAsync(MonthlyRevenues, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetMonthlyAveragesAsync(CancellationToken ct = default) => GetAsync(MonthlyAverages, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetValuationRatiosAsync(CancellationToken ct = default) => GetAsync(ValuationRatios, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetDividendsAsync(CancellationToken ct = default) => GetAsync(Dividends, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetIncomeStatementsAsync(CancellationToken ct = default) => GetAsync(IncomeStatements, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetBalanceSheetsAsync(CancellationToken ct = default) => GetAsync(BalanceSheets, ct);

        public Task<IReadOnlyList<TwseMarketIndex>?> GetMarketIndicesAsync(CancellationToken ct = default) => GetAsync(MarketIndices, ct);

        public Task<IReadOnlyList<TwseDataRow>?> GetMarketDaysAsync(CancellationToken ct = default) => GetAsync(MarketDays, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetMarginTradingAsync(CancellationToken ct = default) => GetAsync(MarginTrading, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetForeignHoldingsTop20Async(CancellationToken ct = default) => GetAsync(ForeignHoldingsTop20, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetSecuritiesLendingAsync(CancellationToken ct = default) => GetAsync(SecuritiesLending, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetDirectorHoldingsAsync(CancellationToken ct = default) => GetAsync(DirectorHoldings, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetMajorShareholdersAsync(CancellationToken ct = default) => GetAsync(MajorShareholders, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetDayTradingSuspensionsAsync(CancellationToken ct = default) => GetAsync(DayTradingSuspensions, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetDailyValuationsAsync(CancellationToken ct = default) => GetAsync(DailyValuations, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetQuarterlyEpsAsync(CancellationToken ct = default) => GetAsync(QuarterlyEps, ct);

        public Task<IReadOnlyList<TwseDataRow>?> GetMaterialInfoAsync(CancellationToken ct = default) => GetAsync(MaterialInfo, ct);

        public Task<IReadOnlyList<TwseDataRow>?> GetHolidayScheduleAsync(CancellationToken ct = default) => GetAsync(HolidaySchedule, ct);

        private Task<IReadOnlyList<T>?> GetAsync<T>(Dataset<T> dataset, CancellationToken ct) where T : class =>
            cache.GetOrCreateSingleFlightAsync($"twse:{dataset.Url}:{time.GetTaipeiToday():yyyyMMdd}", dataset.Ttl,
                () => FetchAsync(dataset), ct, shouldCache: rows => rows is not null && dataset.Ttl > TimeSpan.Zero);

        private async Task<IReadOnlyList<T>?> FetchAsync<T>(Dataset<T> dataset) where T : class
        {
            // Shared by every caller waiting on it, so no one caller's token may stop it.
            var ct = CancellationToken.None;
            try
            {
                var client = httpClientFactory.CreateClient(HttpClientName);
                // Headers per request, not on the client: concurrent requests would race on them.
                using var request = new HttpRequestMessage(HttpMethod.Get, dataset.Url);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("TWSE OpenAPI {Url} returned {Status}", dataset.Url, response.StatusCode);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    logger.LogWarning("TWSE OpenAPI {Url} did not answer a JSON array", dataset.Url);
                    return null;
                }

                var rows = new List<T>();
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object && dataset.Read(element) is { } row)
                        rows.Add(row);
                }

                // Every one of these lists the whole market: no rows means a broken answer.
                if (rows.Count == 0)
                {
                    logger.LogWarning("TWSE OpenAPI {Url} came back without rows", dataset.Url);
                    return null;
                }
                return rows;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TWSE OpenAPI {Url} could not be read", dataset.Url);
                return null;
            }
        }

        /// <summary>A string field, trimmed; empty when missing or not a string.</summary>
        private static string Text(JsonElement row, string field) =>
            row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()!.Trim()
                : "";

        /// <summary>A number TWSE sends as text ("1,050.00"); null when blank, "-" or "N/A".</summary>
        internal static decimal? Number(JsonElement row, string field)
        {
            var text = Text(row, field);
            if (text is "" or "-" or "N/A") return null;
            text = text.Replace(",", "").Replace(" ", "");
            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : null;
        }

        /// <summary>One string per field name across every row and dataset (a few hundred names in all),
        /// instead of one per field of every row; trimmed, as TWSE pads a few.</summary>
        private static readonly ConcurrentDictionary<string, string> FieldNames = new();

        private static IReadOnlyList<KeyValuePair<string, string>> FieldsOf(JsonElement row) =>
            row.EnumerateObject()
                .Select(p => KeyValuePair.Create(FieldNames.GetOrAdd(p.Name, static name => name.Trim()), p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "null",
                    _ => p.Value.GetRawText(),
                }))
                .ToArray(); // exactly as long as the row: these are held for hours, thousands at a time
    }
}
