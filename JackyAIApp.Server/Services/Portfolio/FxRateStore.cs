using System.Text.Json;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>One published rate, as an FX source hands it over before it is persisted.</summary>
    public readonly record struct PublishedFxRate(DateOnly Date, string Currency, decimal Buy, decimal Sell);

    /// <summary>
    /// The <see cref="FxRate"/> reads and writes every FX source needs, parameterised by
    /// <see cref="FxRateSources"/>.
    ///
    /// The sources themselves have nothing in common — 集保 is probed one date at a time and answers
    /// 404 for anything unpublished, 國泰 answers a whole range behind an anti-forgery token — but
    /// what they do with the result is identical: look up the latest stored rate on or before a
    /// date, and insert the published days not already held. Keeping that here means a third source
    /// only has to describe how it fetches.
    /// </summary>
    public sealed class FxRateStore(AzureSQLDBContext db, string source, ILogger logger)
    {
        /// <summary>
        /// Latest stored buy rate for <paramref name="currency"/> on or before <paramref name="asOf"/>,
        /// no earlier than <paramref name="earliest"/>. Null when nothing is stored in that window.
        /// </summary>
        public async Task<(DateOnly Date, decimal Rate)?> LatestBuyRateAsync(
            string currency, DateOnly earliest, DateOnly asOf, CancellationToken ct)
        {
            var row = await db.FxRates.AsNoTracking()
                .Where(r => r.Source == source && r.Currency == currency
                    && r.RateDate <= asOf && r.RateDate >= earliest)
                .OrderByDescending(r => r.RateDate)
                .Select(r => new { r.RateDate, r.BuyRate })
                .FirstOrDefaultAsync(ct);
            return row == null ? null : (row.RateDate, row.BuyRate);
        }

        /// <summary>
        /// Persists the rates not already stored for this source. A published rate never changes, so
        /// rows already held are left alone rather than overwritten.
        /// </summary>
        public async Task AddMissingAsync(IReadOnlyCollection<PublishedFxRate> rates, CancellationToken ct)
        {
            if (rates.Count == 0) return;

            var dates = rates.Select(r => r.Date).Distinct().ToList();
            var currencies = rates.Select(r => r.Currency).Distinct().ToList();
            var existing = await db.FxRates.AsNoTracking()
                .Where(r => r.Source == source
                    && dates.Contains(r.RateDate) && currencies.Contains(r.Currency))
                .Select(r => new { r.RateDate, r.Currency })
                .ToListAsync(ct);
            var have = existing.Select(e => (e.RateDate, e.Currency)).ToHashSet();

            var added = rates
                .Where(r => !have.Contains((r.Date, r.Currency)))
                .Select(r => new FxRate
                {
                    RateDate = r.Date,
                    Currency = r.Currency,
                    Source = source,
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
                // Two concurrent requests can race to insert the same published day; the unique
                // index makes the loser redundant, not wrong.
                logger.LogDebug(ex, "{Source} FX rates already stored by a concurrent request", source);
            }
        }
    }

    /// <summary>Parsing shared by the FX sources, which all quote their rates as JSON strings.</summary>
    public static class FxRateJson
    {
        /// <summary>Reads a rate held as a JSON string, e.g. <c>"31.657500"</c>. False on anything else.</summary>
        public static bool TryReadRate(JsonElement row, string property, out decimal value)
        {
            value = 0;
            return row.TryGetProperty(property, out var el)
                && el.ValueKind == JsonValueKind.String
                && decimal.TryParse(el.GetString(), out value);
        }
    }
}
