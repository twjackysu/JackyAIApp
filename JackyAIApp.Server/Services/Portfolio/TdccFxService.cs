using System.Text;
using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Portfolio
{
    public interface ITdccFxService
    {
        /// <summary>
        /// 集保 buy rate (foreign → TWD) for <paramref name="currency"/> as of the given date,
        /// falling back to the most recent published date on or before it. Null when nothing has
        /// ever been published for that currency on or before the date.
        /// </summary>
        Task<decimal?> GetBuyRateAsync(string currency, DateOnly asOf, CancellationToken ct = default);

        /// <summary>
        /// Buy rates for every date in the window, already carried forward, so a caller walking a
        /// date range does one lookup instead of one per day. Dates before the first published
        /// rate are absent rather than guessed.
        /// </summary>
        Task<IReadOnlyDictionary<DateOnly, decimal>> GetBuyRateSeriesAsync(
            string currency, DateOnly from, DateOnly to, CancellationToken ct = default);
    }

    /// <summary>
    /// Reads 集保結算所's published fund-settlement FX rates, persisting each published day once.
    ///
    /// The publication schedule is deliberately not modelled. 集保 states the batch runs "下午二時後"
    /// but "依每日批次作業完成時間而異" and "並非於固定時點", so any hardcoded cutover hour is wrong
    /// on some days. Instead the endpoint is the source of truth: it answers 404 for a date that is
    /// not published — which covers "too early in the day", weekends, and holidays with one rule —
    /// and 200 once it is. Callers get the latest published rate on or before the date they asked
    /// for, which is exactly what 基富通 itself displays.
    /// </summary>
    public class TdccFxService(
        AzureSQLDBContext db,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<TdccFxService> logger) : ITdccFxService
    {
        private const string QUERY_URL = "https://www.fundclear.com.tw/api/announce/query-exchange-rate";

        /// <summary>
        /// How far back to look for a carried-forward rate before giving up. Comfortably clears a
        /// long national holiday plus a weekend; beyond that, silence means we genuinely have no
        /// rate rather than a closed market, and guessing from a stale one would be worse.
        /// </summary>
        private const int MaxCarryForwardDays = 14;

        /// <summary>
        /// Business days probed backwards from the requested date when nothing is stored yet.
        /// Bounded so a cold cache costs a handful of requests, never a crawl.
        /// </summary>
        private const int MaxProbeDays = 10;

        public async Task<decimal?> GetBuyRateAsync(string currency, DateOnly asOf, CancellationToken ct = default)
        {
            currency = (currency ?? "").Trim().ToUpperInvariant();
            if (currency.Length == 0) return null;

            var earliest = asOf.AddDays(-MaxCarryForwardDays);
            var stored = await LatestStoredAsync(currency, earliest, asOf, ct);
            if (stored != null && stored.Value.Date == asOf) return stored.Value.Rate;

            // Nothing for the exact date yet. Probe backwards from it: the first 200 wins, and
            // every date probed is either persisted or known-empty, so this converges after the
            // first call of the day.
            var probes = 0;
            for (var d = asOf; probes < MaxProbeDays && d >= earliest; d = d.AddDays(-1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue; // never published
                probes++;

                if (stored != null && d <= stored.Value.Date) break; // stored value is the best available
                var fetched = await FetchAndStoreAsync(d, ct);
                if (fetched != null && fetched.TryGetValue(currency, out var rate)) return rate;
            }

            return stored?.Rate;
        }

        public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetBuyRateSeriesAsync(
            string currency, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            currency = (currency ?? "").Trim().ToUpperInvariant();
            var result = new Dictionary<DateOnly, decimal>();
            if (currency.Length == 0 || to < from) return result;

            // Reach back before the window so its first days can carry forward from an earlier rate.
            var rows = await db.FxRates.AsNoTracking()
                .Where(r => r.Source == FxRateSources.Tdcc && r.Currency == currency
                    && r.RateDate >= from.AddDays(-MaxCarryForwardDays) && r.RateDate <= to)
                .OrderBy(r => r.RateDate)
                .Select(r => new { r.RateDate, r.BuyRate })
                .ToListAsync(ct);
            if (rows.Count == 0) return result;

            var idx = 0;
            decimal? current = null;
            for (var d = from.AddDays(-MaxCarryForwardDays); d <= to; d = d.AddDays(1))
            {
                while (idx < rows.Count && rows[idx].RateDate <= d)
                {
                    current = rows[idx].BuyRate;
                    idx++;
                }
                if (d >= from && current != null) result[d] = current.Value;
            }
            return result;
        }

        private async Task<(DateOnly Date, decimal Rate)?> LatestStoredAsync(
            string currency, DateOnly earliest, DateOnly asOf, CancellationToken ct)
        {
            var row = await db.FxRates.AsNoTracking()
                .Where(r => r.Source == FxRateSources.Tdcc && r.Currency == currency
                    && r.RateDate <= asOf && r.RateDate >= earliest)
                .OrderByDescending(r => r.RateDate)
                .Select(r => new { r.RateDate, r.BuyRate })
                .FirstOrDefaultAsync(ct);
            return row == null ? null : (row.RateDate, row.BuyRate);
        }

        /// <summary>
        /// Fetches one date and persists every currency it returns. Returns null when 集保 has no
        /// rate for that date (404 — unpublished, weekend, or holiday), which the caller treats as
        /// "look further back", not as an error. A negative result is remembered in memory only:
        /// today's 404 becomes a 200 later the same afternoon, so it must not be persisted.
        /// </summary>
        private async Task<Dictionary<string, decimal>?> FetchAndStoreAsync(DateOnly date, CancellationToken ct)
        {
            var missKey = $"tdcc_fx_miss_{date:yyyyMMdd}";
            if (cache.TryGetValue(missKey, out bool _)) return null;

            try
            {
                var client = httpClientFactory.CreateClient("ExternalReadOnly");
                var payload = JsonSerializer.Serialize(new { dataDate = date.ToString("yyyy/MM/dd") });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(QUERY_URL, content, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Unpublished today, or simply not a business day. Re-check soon if it's recent.
                    cache.Set(missKey, true, CacheTtl.Portfolio.Fx);
                    return null;
                }
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                var parsed = Parse(json);
                if (parsed.Count == 0)
                {
                    cache.Set(missKey, true, CacheTtl.Portfolio.Fx);
                    return null;
                }

                await StoreAsync(date, parsed, ct);
                return parsed.ToDictionary(p => p.Key, p => p.Value.Buy);
            }
            catch (Exception ex)
            {
                // Fail soft: the caller falls back to a carried-forward or mid-market rate rather
                // than failing the whole portfolio view over an FX lookup.
                logger.LogWarning(ex, "Failed to fetch 集保 FX rates for {Date}", date);
                return null;
            }
        }

        /// <summary>
        /// The response is a flat array of {currencyName, buyRate, sellRate}, where currencyName is
        /// a display string with the ISO code in brackets — "美元(USD)". Rates arrive as strings.
        /// </summary>
        private static Dictionary<string, (decimal Buy, decimal Sell)> Parse(string json)
        {
            var result = new Dictionary<string, (decimal, decimal)>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("currencyName", out var nameEl)) continue;
                var name = nameEl.GetString() ?? "";
                var open = name.IndexOf('(');
                var close = name.IndexOf(')');
                if (open < 0 || close <= open + 1) continue;
                var code = name[(open + 1)..close].Trim().ToUpperInvariant();
                if (code.Length != 3) continue;

                if (!TryRate(row, "buyRate", out var buy) || !TryRate(row, "sellRate", out var sell)) continue;
                if (buy <= 0) continue;
                result[code] = (buy, sell);
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
            DateOnly date, Dictionary<string, (decimal Buy, decimal Sell)> rates, CancellationToken ct)
        {
            var existing = await db.FxRates
                .Where(r => r.Source == FxRateSources.Tdcc && r.RateDate == date)
                .Select(r => r.Currency)
                .ToListAsync(ct);
            var have = existing.ToHashSet();

            var added = rates
                .Where(kv => !have.Contains(kv.Key))
                .Select(kv => new FxRate
                {
                    RateDate = date,
                    Currency = kv.Key,
                    Source = FxRateSources.Tdcc,
                    BuyRate = kv.Value.Buy,
                    SellRate = kv.Value.Sell,
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
                // Two concurrent requests can race to insert the same published day; the unique
                // index makes the loser redundant, not wrong.
                logger.LogDebug(ex, "集保 FX rates for {Date} already stored by a concurrent request", date);
            }
        }
    }
}
