using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Finance;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.ExternalApi.Twse
{
    /// <summary>
    /// A row of a TWSE OpenAPI dataset. <see cref="Fields"/> is every field as TWSE sent it, in its
    /// order — what an AI prompt quotes; the typed properties of each row type are the ones code reads.
    /// </summary>
    public abstract record TwseRow(IReadOnlyList<KeyValuePair<string, string>> Fields)
    {
        /// <summary>Every field as a "field: value" line, in TWSE's order — how a prompt quotes a row.</summary>
        public string ToText()
        {
            var text = new StringBuilder();
            foreach (var (name, value) in Fields)
                text.AppendLine($"{name}: {value}");
            return text.ToString();
        }
    }

    /// <summary>One stock's row of a dataset that is only ever quoted whole (monthly average price,
    /// valuation ratios, dividends, income statement, balance sheet).</summary>
    public sealed record TwseStockRow(string Code, IReadOnlyList<KeyValuePair<string, string>> Fields)
        : TwseRow(Fields);

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

        private sealed record Dataset<T>(string Url, TimeSpan Ttl, Func<JsonElement, T?> Read) where T : class;

        private static readonly Dataset<TwseDailyQuote> DailyQuotes = new(DailyQuotesUrl, TimeSpan.FromHours(4),
            row => Text(row, "Code") is { Length: > 0 } code
                ? new TwseDailyQuote(code, Text(row, "Name"), Number(row, "ClosingPrice"), FieldsOf(row))
                : null);

        // Company data rarely changes: a day is fresh enough.
        private static readonly Dataset<TwseCompanyProfile> CompanyProfiles = new(CompanyProfilesUrl, TimeSpan.FromHours(24),
            row => Text(row, "公司代號") is { Length: > 0 } code
                ? new TwseCompanyProfile(code, Text(row, "公司名稱"), FieldsOf(row))
                : null);

        private static readonly Dataset<TwseMonthlyRevenue> MonthlyRevenues = new(MonthlyRevenuesUrl, TimeSpan.FromHours(4),
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

        private static readonly Dataset<TwseMarketIndex> MarketIndices = new(MarketIndicesUrl, TimeSpan.FromHours(4),
            row => Text(row, "指數") is { Length: > 0 } name ? new TwseMarketIndex(name, FieldsOf(row)) : null);

        private static Dataset<TwseStockRow> StockRows(string url, string codeField, bool dropWarrants = false) => new(url, TimeSpan.FromHours(4),
            row => Text(row, codeField) is { Length: > 0 } code && !(dropWarrants && TwStockCode.IsWarrant(code))
                ? new TwseStockRow(code, FieldsOf(row))
                : null);

        public Task<IReadOnlyList<TwseDailyQuote>?> GetDailyQuotesAsync(CancellationToken ct = default) => GetAsync(DailyQuotes, ct);

        public Task<IReadOnlyList<TwseCompanyProfile>?> GetCompanyProfilesAsync(CancellationToken ct = default) => GetAsync(CompanyProfiles, ct);

        public Task<IReadOnlyList<TwseMonthlyRevenue>?> GetMonthlyRevenuesAsync(CancellationToken ct = default) => GetAsync(MonthlyRevenues, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetMonthlyAveragesAsync(CancellationToken ct = default) => GetAsync(MonthlyAverages, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetValuationRatiosAsync(CancellationToken ct = default) => GetAsync(ValuationRatios, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetDividendsAsync(CancellationToken ct = default) => GetAsync(Dividends, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetIncomeStatementsAsync(CancellationToken ct = default) => GetAsync(IncomeStatements, ct);

        public Task<IReadOnlyList<TwseStockRow>?> GetBalanceSheetsAsync(CancellationToken ct = default) => GetAsync(BalanceSheets, ct);

        public Task<IReadOnlyList<TwseMarketIndex>?> GetMarketIndicesAsync(CancellationToken ct = default) => GetAsync(MarketIndices, ct);

        private Task<IReadOnlyList<T>?> GetAsync<T>(Dataset<T> dataset, CancellationToken ct) where T : class =>
            cache.GetOrCreateSingleFlightAsync($"twse:{dataset.Url}:{time.GetTaipeiToday():yyyyMMdd}", dataset.Ttl,
                () => FetchAsync(dataset), ct, shouldCache: rows => rows is not null);

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
        /// instead of one per field of every row: STOCK_DAY_AVG_ALL alone has ~34,000 rows.</summary>
        private static readonly ConcurrentDictionary<string, string> FieldNames = new();

        private static IReadOnlyList<KeyValuePair<string, string>> FieldsOf(JsonElement row) =>
            row.EnumerateObject()
                .Select(p => KeyValuePair.Create(FieldNames.GetOrAdd(p.Name, p.Name), p.Value.ValueKind switch
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
