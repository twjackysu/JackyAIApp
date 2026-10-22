using System.Globalization;
using System.Text.Json;
using JackyAIApp.Server.Common;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.ExternalApi.Twse
{
    /// <summary>
    /// A row of a TWSE OpenAPI dataset. <see cref="Fields"/> is every field as TWSE sent it, in its
    /// order — what an AI prompt quotes; the typed properties of each row type are the ones code reads.
    /// </summary>
    public abstract record TwseRow(IReadOnlyList<KeyValuePair<string, string>> Fields);

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

        public Task<IReadOnlyList<TwseDailyQuote>?> GetDailyQuotesAsync(CancellationToken ct = default) => GetAsync(DailyQuotes, ct);

        public Task<IReadOnlyList<TwseCompanyProfile>?> GetCompanyProfilesAsync(CancellationToken ct = default) => GetAsync(CompanyProfiles, ct);

        public Task<IReadOnlyList<TwseMonthlyRevenue>?> GetMonthlyRevenuesAsync(CancellationToken ct = default) => GetAsync(MonthlyRevenues, ct);

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

        private static IReadOnlyList<KeyValuePair<string, string>> FieldsOf(JsonElement row) =>
            row.EnumerateObject()
                .Select(p => KeyValuePair.Create(p.Name, p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "null",
                    _ => p.Value.GetRawText(),
                }))
                .ToList();
    }
}
