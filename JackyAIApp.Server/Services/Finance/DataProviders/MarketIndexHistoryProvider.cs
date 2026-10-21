using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Six years of daily <c>^TWII</c> closes from Yahoo Finance's unofficial v8 chart endpoint (the
    /// same one <see cref="MarketOverviewProvider"/> uses for today's quote). One request, no
    /// caching here — <see cref="CachedMarketIndexHistoryProvider"/> wraps it.
    ///
    /// Yahoo has no SLA, so nothing it returns is trusted blindly: the series is cleaned up, then
    /// checked as a whole (<see cref="Parse"/>). A series that fails any check is rejected outright
    /// — it is never patched — so a bad day upstream shows as "unavailable", not as a wrong chart.
    /// The build environment cannot reach Yahoo; the shape is verified by
    /// <c>YahooChartContractTests</c> on the scheduled workflow.
    /// </summary>
    public class MarketIndexHistoryProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<MarketIndexHistoryProvider> logger,
        TimeProvider? timeProvider = null) : IMarketIndexHistoryProvider
    {
        internal const string Symbol = "^TWII";
        internal const string DisplayName = "加權指數（價格指數，不含息）";
        internal const string SourceName = "Yahoo Finance";
        internal static readonly string Url = $"{YahooChart.ChartBase}/%5ETWII?range=6y&interval=1d";

        /// <summary>Six years of trading days is about 1,450; far fewer means a truncated reply.</summary>
        internal const int MinPoints = 1000;

        /// <summary>The newest point may be at most this many days (Taipei) before today — longer than
        /// any market closure, so a lapse this long is the source being stale, not the market resting.</summary>
        internal const int MaxStaleDays = 10;

        /// <summary>Largest day-to-day move believed. The exchange stops a day at ±10% and the TAIEX's
        /// largest ever is about ±9.7%, so beyond this is bad data rather than a real move.</summary>
        internal const decimal MaxDailyMove = 0.15m;

        /// <summary>From this time (Taipei) on a trading day, that day's bar is the final close.</summary>
        internal static readonly TimeSpan FinalAfter = TimeSpan.FromHours(14);

        // Yahoo sends the TAIEX as doubles with float noise (17754.490234375); well under this in practice.
        private const double MaxPlausibleClose = 1e9;

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        public async Task<MarketIndexHistoryResponse?> GetAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var client = httpClientFactory.CreateClient("ExternalReadOnly");
                using var request = YahooChart.Request(Url);

                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Yahoo {symbol} history returned {status}", Symbol, response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var (history, rejection) = Parse(json, _time.GetUtcNow());
                if (history is null)
                    logger.LogWarning("Yahoo {symbol} history rejected: {reason}", Symbol, rejection);
                return history;
            }
            // A caller walking away is not a failure; a timeout (also an OperationCanceledException) is.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Failed to fetch Yahoo {symbol} history", Symbol);
                return null;
            }
        }

        /// <summary>
        /// Turns Yahoo's chart JSON into the series, or says why not. Per day: skips a missing,
        /// non-finite or non-positive close; dates it by the Taipei calendar (UTC+8, no DST); keeps
        /// the latest-stamped bar when a date appears twice; drops today's bar while it is still
        /// forming. Then the whole series must pass the sanity checks.
        /// </summary>
        internal static (MarketIndexHistoryResponse? History, string? Rejection) Parse(string json, DateTimeOffset nowUtc)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch (JsonException) { return (null, "response is not JSON"); }

            using (doc)
            {
                if (!TryGetArrays(doc.RootElement, out var timestamps, out var closes))
                    return (null, "chart.result[0] has no timestamp[] / indicators.quote[0].close[] arrays");
                if (timestamps.GetArrayLength() != closes.GetArrayLength())
                    return (null, $"timestamp[] has {timestamps.GetArrayLength()} entries but close[] has {closes.GetArrayLength()}");

                var taipeiNow = TaipeiTime.ToTaipei(nowUtc);
                var today = DateOnly.FromDateTime(taipeiNow);

                // The day's open/high/low are optional extras for the trendline chart: missing or
                // inconsistent ones are dropped for that day, never a reason to reject the series.
                var quote = doc.RootElement.GetProperty("chart").GetProperty("result")[0]
                    .GetProperty("indicators").GetProperty("quote")[0];
                var opens = OptionalArray(quote, "open", timestamps.GetArrayLength());
                var highs = OptionalArray(quote, "high", timestamps.GetArrayLength());
                var lows = OptionalArray(quote, "low", timestamps.GetArrayLength());

                // date -> (the bar's own timestamp, its close, its range). A later timestamp wins a same-date clash.
                var byDate = new SortedDictionary<DateOnly, (long Stamp, decimal Close, (decimal O, decimal H, decimal L)? Range)>();
                var stamps = timestamps.EnumerateArray().GetEnumerator();
                var values = closes.EnumerateArray().GetEnumerator();
                var index = -1;
                while (stamps.MoveNext() && values.MoveNext())
                {
                    index++;
                    if (stamps.Current.ValueKind != JsonValueKind.Number || !stamps.Current.TryGetInt64(out var stamp)) continue;
                    if (values.Current.ValueKind != JsonValueKind.Number || !values.Current.TryGetDouble(out var raw)) continue;
                    if (!double.IsFinite(raw) || raw <= 0 || raw >= MaxPlausibleClose) continue;

                    var close = Math.Round((decimal)raw, 2);
                    if (close <= 0) continue;

                    DateOnly date;
                    try { date = DateOnly.FromDateTime(TaipeiTime.ToTaipei(DateTimeOffset.FromUnixTimeSeconds(stamp))); }
                    catch (ArgumentOutOfRangeException) { continue; }

                    if (!byDate.TryGetValue(date, out var existing) || stamp >= existing.Stamp)
                        byDate[date] = (stamp, close, Range(opens, highs, lows, index, close));
                }

                // Yahoo includes the bar in progress. Before the close is final, or on a weekend, it is not a close.
                if (!IsFinal(today, taipeiNow.TimeOfDay))
                    byDate.Remove(today);

                var points = byDate.Select(kv => new MarketIndexPoint(kv.Key, kv.Value.Close)
                {
                    Open = kv.Value.Range?.O,
                    High = kv.Value.Range?.H,
                    Low = kv.Value.Range?.L,
                }).ToList();

                if (points.Count < MinPoints)
                    return (null, $"only {points.Count} usable points (need at least {MinPoints})");

                var last = points[^1].Date;
                if (today.DayNumber - last.DayNumber > MaxStaleDays)
                    return (null, $"newest point {last:yyyy-MM-dd} is more than {MaxStaleDays} days before today ({today:yyyy-MM-dd})");

                for (var i = 1; i < points.Count; i++)
                {
                    var move = Math.Abs(points[i].Close / points[i - 1].Close - 1);
                    if (move > MaxDailyMove)
                        return (null, $"{points[i - 1].Date:yyyy-MM-dd} -> {points[i].Date:yyyy-MM-dd} moves {move:P1} in one day (limit {MaxDailyMove:P0})");
                }

                return (new MarketIndexHistoryResponse(Symbol, DisplayName, SourceName, last, points), null);
            }
        }

        /// <summary><paramref name="name"/>[] of Yahoo's quote, or null when it is absent or not as long as timestamp[].</summary>
        private static JsonElement? OptionalArray(JsonElement quote, string name, int length) =>
            quote.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array && array.GetArrayLength() == length
                ? array
                : null;

        /// <summary>The day's open/high/low, or null unless all three are positive and consistent with the close.</summary>
        private static (decimal O, decimal H, decimal L)? Range(
            JsonElement? opens, JsonElement? highs, JsonElement? lows, int index, decimal close)
        {
            static decimal? At(JsonElement? array, int i) =>
                array is { } a && a[i].ValueKind == JsonValueKind.Number && a[i].TryGetDouble(out var v)
                && double.IsFinite(v) && v > 0 && v < MaxPlausibleClose
                    ? Math.Round((decimal)v, 2)
                    : null;

            if (At(opens, index) is not { } o || At(highs, index) is not { } h || At(lows, index) is not { } l)
                return null;
            return h >= Math.Max(o, close) && l <= Math.Min(o, close) ? (o, h, l) : null;
        }

        /// <summary>True when the bar dated <paramref name="today"/> is a settled close: a weekday, from 14:00 on.</summary>
        private static bool IsFinal(DateOnly today, TimeSpan timeOfDay) =>
            today.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && timeOfDay >= FinalAfter;

        private static bool TryGetArrays(JsonElement root, out JsonElement timestamps, out JsonElement closes)
        {
            timestamps = closes = default;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("chart", out var chart) || chart.ValueKind != JsonValueKind.Object ||
                !chart.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
                return false;

            var first = result[0];
            if (first.ValueKind != JsonValueKind.Object ||
                !first.TryGetProperty("timestamp", out timestamps) || timestamps.ValueKind != JsonValueKind.Array ||
                !first.TryGetProperty("indicators", out var indicators) || indicators.ValueKind != JsonValueKind.Object ||
                !indicators.TryGetProperty("quote", out var quote) ||
                quote.ValueKind != JsonValueKind.Array || quote.GetArrayLength() == 0 ||
                quote[0].ValueKind != JsonValueKind.Object ||
                !quote[0].TryGetProperty("close", out closes) || closes.ValueKind != JsonValueKind.Array)
                return false;

            return true;
        }
    }
}
