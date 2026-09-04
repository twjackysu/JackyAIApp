using System.Text.Json;
using System.Text.RegularExpressions;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Portfolio
{
    public interface ICathayFxService
    {
        /// <summary>
        /// 國泰世華 spot buy rate (foreign → TWD) published FOR <paramref name="rateDate"/>, falling
        /// back to the most recent business day before it. Null when nothing is published in range.
        ///
        /// Callers pass the date the PRICE belongs to, not today: 國泰 values a holding at
        /// 股數 × 最新價 × 昨收匯率, so the rate has to be the one that goes with that close.
        /// </summary>
        Task<decimal?> GetBuyRateAsync(string currency, DateOnly rateDate, CancellationToken ct = default);
    }

    /// <summary>
    /// Reads 國泰世華's published board rates, persisting each business day once.
    ///
    /// Why this source and not a mid-market feed: 國泰 複委託 converts US holdings at its own bank's
    /// board rate, and its app documents the formula as 總預估現值 = 股數 × 最新價 × 昨收匯率.
    /// Reconstructing that rate from four independent observations (two symbols each, so the FX is
    /// over-determined) landed on the published buy rate for the close's own date every time —
    /// exact on 2026/08/26 and 2026/09/03, within 0.01 on 08/28 and 09/01 (the endpoint publishes
    /// one value per day, while the bank's board moves intraday). A mid-market rate was ~0.17 off,
    /// which on this portfolio was ~300 TWD of phantom P&amp;L.
    ///
    /// The endpoint is ASP.NET anti-forgery protected, so each fetch first loads the public board
    /// page for a token and cookie. It answers a whole date range in one call, which is why this
    /// fills a window rather than probing day by day the way <see cref="TdccFxService"/> has to.
    /// </summary>
    public partial class CathayFxService(
        AzureSQLDBContext db,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<CathayFxService> logger) : ICathayFxService
    {
        private const string BOARD_PAGE_URL =
            "https://www.cathaybk.com.tw/cathaybk/personal/product/deposit/currency-billboard/";
        private const string HISTORY_URL =
            "https://www.cathaybk.com.tw/CathayBK/Web/service/ForiegnHisRateService.ashx";

        /// <summary>"3" is 即期 (spot); "2" is 現鈔 (cash), which is not the settlement rate.</summary>
        private const string SpotCurrencyType = "3";

        /// <summary>
        /// How far back a lookup may carry forward, and how wide a window to fill on a miss. Clears
        /// a long national holiday plus a weekend; past that, no rate means no rate rather than a
        /// closed market, and stretching an older one further would be a guess.
        /// </summary>
        private const int WindowDays = 14;

        [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
        private static partial Regex TokenRegex();

        public async Task<decimal?> GetBuyRateAsync(string currency, DateOnly rateDate, CancellationToken ct = default)
        {
            currency = (currency ?? "").Trim().ToUpperInvariant();
            if (currency.Length != 3) return null;

            var earliest = rateDate.AddDays(-WindowDays);
            var stored = await LatestStoredAsync(currency, earliest, rateDate, ct);
            if (stored != null && stored.Value.Date == rateDate) return stored.Value.Rate;

            // One call covers the whole window, so a miss costs a single fetch no matter how many
            // days are absent. Negative results live in memory only: today's rate appears later the
            // same day, and persisting "nothing here" would outlive that.
            var missKey = $"cathay_fx_miss_{currency}_{rateDate:yyyyMMdd}";
            if (!cache.TryGetValue(missKey, out bool _))
            {
                var fetched = await FetchAndStoreAsync(currency, earliest, rateDate, ct);
                if (fetched)
                {
                    var refreshed = await LatestStoredAsync(currency, earliest, rateDate, ct);
                    if (refreshed != null) return refreshed.Value.Rate;
                }
                cache.Set(missKey, true, CacheTtl.Portfolio.Fx);
            }

            // Nothing published for the exact date — the most recent business day before it is the
            // right answer, which is also what 國泰 shows over a weekend.
            return stored?.Rate;
        }

        private async Task<(DateOnly Date, decimal Rate)?> LatestStoredAsync(
            string currency, DateOnly earliest, DateOnly asOf, CancellationToken ct)
        {
            var row = await db.FxRates.AsNoTracking()
                .Where(r => r.Source == FxRateSources.Cathay && r.Currency == currency
                    && r.RateDate <= asOf && r.RateDate >= earliest)
                .OrderByDescending(r => r.RateDate)
                .Select(r => new { r.RateDate, r.BuyRate })
                .FirstOrDefaultAsync(ct);
            return row == null ? null : (row.RateDate, row.BuyRate);
        }

        /// <summary>Fetches a date range and persists every business day it returns. False on any failure.</summary>
        private async Task<bool> FetchAndStoreAsync(
            string currency, DateOnly from, DateOnly to, CancellationToken ct)
        {
            try
            {
                var client = httpClientFactory.CreateClient("ExternalReadOnly");

                var (token, cookie) = await GetAntiForgeryAsync(client, ct);
                if (token == null) return false;

                using var request = new HttpRequestMessage(HttpMethod.Post, HISTORY_URL)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["currency"] = currency,
                        ["curr_type"] = SpotCurrencyType,
                        ["from_date"] = from.ToString("yyyy/MM/dd"),
                        ["to_date"] = to.ToString("yyyy/MM/dd"),
                        ["Token"] = token,
                    }),
                };
                request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                request.Headers.TryAddWithoutValidation("Referer", BOARD_PAGE_URL);
                if (cookie != null) request.Headers.TryAddWithoutValidation("Cookie", cookie);

                using var response = await client.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                var parsed = Parse(await response.Content.ReadAsStringAsync(ct));
                if (parsed.Count == 0) return false;

                await StoreAsync(currency, parsed, ct);
                return true;
            }
            catch (Exception ex)
            {
                // Fail soft: the caller falls back to a mid-market rate rather than failing the
                // whole portfolio view over an FX lookup.
                logger.LogWarning(ex, "Failed to fetch 國泰世華 FX rates for {Currency} {From}..{To}", currency, from, to);
                return false;
            }
        }

        /// <summary>
        /// The rate endpoint rejects a request without a matching anti-forgery token and cookie, so
        /// the public board page is loaded first purely to harvest them. Cached briefly — the pair
        /// is reusable, and re-fetching a 190KB page per rate lookup would dwarf the actual call.
        /// </summary>
        private async Task<(string? Token, string? Cookie)> GetAntiForgeryAsync(HttpClient client, CancellationToken ct)
        {
            const string key = "cathay_fx_antiforgery";
            if (cache.TryGetValue(key, out (string Token, string? Cookie) cached)) return cached;

            using var response = await client.GetAsync(BOARD_PAGE_URL, ct);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(ct);

            var match = TokenRegex().Match(html);
            if (!match.Success)
            {
                logger.LogWarning("No anti-forgery token on the 國泰世華 board page; markup may have changed");
                return (null, null);
            }

            var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
                ? string.Join("; ", values.Select(v => v.Split(';')[0]))
                : null;

            var pair = (match.Groups[1].Value, cookie);
            cache.Set(key, pair, CacheTtl.Portfolio.Fx);
            return pair;
        }

        /// <summary>
        /// Response is a flat array of {Name, CurDate, BuyRate, SellRate}, dates as yyyy/MM/dd and
        /// rates as strings. Business days only — weekends and holidays are simply absent.
        /// </summary>
        private static List<(DateOnly Date, decimal Buy, decimal Sell)> Parse(string json)
        {
            var result = new List<(DateOnly, decimal, decimal)>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("CurDate", out var dateEl)) continue;
                if (!DateOnly.TryParseExact(dateEl.GetString(), "yyyy/MM/dd", out var date)) continue;
                if (!TryRate(row, "BuyRate", out var buy) || !TryRate(row, "SellRate", out var sell)) continue;
                if (buy <= 0) continue;
                result.Add((date, buy, sell));
            }
            return result;
        }

        private static bool TryRate(JsonElement row, string property, out decimal value)
        {
            value = 0;
            return row.TryGetProperty(property, out var el)
                && el.ValueKind == JsonValueKind.String
                && decimal.TryParse(el.GetString(), out value);
        }

        private async Task StoreAsync(
            string currency, List<(DateOnly Date, decimal Buy, decimal Sell)> rates, CancellationToken ct)
        {
            var dates = rates.Select(r => r.Date).ToList();
            var have = await db.FxRates
                .Where(r => r.Source == FxRateSources.Cathay && r.Currency == currency && dates.Contains(r.RateDate))
                .Select(r => r.RateDate)
                .ToListAsync(ct);
            var known = have.ToHashSet();

            var added = rates
                .Where(r => !known.Contains(r.Date))
                .Select(r => new FxRate
                {
                    RateDate = r.Date,
                    Currency = currency,
                    Source = FxRateSources.Cathay,
                    BuyRate = r.Buy,
                    SellRate = r.Sell,
                })
                .ToList();
            if (added.Count == 0) return;

            db.FxRates.AddRange(added);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Concurrent requests can race to insert the same day; the unique index makes the
                // loser redundant, not wrong.
                logger.LogDebug(ex, "國泰世華 FX rates for {Currency} already stored by a concurrent request", currency);
            }
        }
    }
}
