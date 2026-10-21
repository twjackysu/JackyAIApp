using System.Globalization;
using System.Text;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <summary>The market-wide data the /finance page shows, whichever parts could be had.</summary>
    public sealed record MarketBackgroundData(
        MarketOverviewResponse? Overview,
        MacroEconomyResponse? Macro,
        MarketIndexHistoryResponse? TaiexHistory,
        IndicatorExplorerResponse? Indicators);

    public interface IMarketBackground
    {
        /// <summary>
        /// The /finance page's market data, tidied into text for the assistant's system prompt; null
        /// when none of it could be had. The same text for every caller until it is rebuilt, so the
        /// prompt that carries it can be cached across conversations.
        /// </summary>
        Task<string?> GetAsync(CancellationToken ct);
    }

    /// <summary>
    /// Background for the AI assistant: what the /finance page shows above its analysis tabs —
    /// the market overview tiles, sector indices, margin trading, bank rates, the FRED indicators,
    /// and the TAIEX history behind the seasonal and trendline cards — boiled down to figures. Much
    /// of it (US markets, gold, BTC, US rates) is outside what the tw-stock tools can look up. The
    /// analysis tabs (綜合量化分析, AI 趨勢分析, 今日市場AI摘要) are left out: they are per-stock or
    /// AI-written, not market data.
    /// <para>
    /// Written for few tokens, in the manner of TOON: each table is one header naming its fields —
    /// <c>name[rows]{field,field}:</c> — then one comma-separated line per row; numbers carry no
    /// thousands separators and percentages no % sign (the field name says so). Every turn sends it.
    /// </para>
    /// </summary>
    public class MarketBackground(
        IMarketDataQueryService marketData,
        IMemoryCache cache,
        ILogger<MarketBackground> logger,
        TimeProvider? timeProvider = null) : IMarketBackground
    {
        /// <summary>
        /// How long one rendering is kept. Long enough that conversations share a cached prompt, short
        /// enough that intraday quotes are not too stale; each rebuild costs every conversation one
        /// cache write.
        /// </summary>
        internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

        /// <summary>Each source may take this long before the background goes without it: a chat
        /// turn does not wait on a slow upstream.</summary>
        private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(4);

        private const string CacheKey = "ChatMarketBackground";
        private static readonly SemaphoreSlim BuildLock = new(1, 1);
        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        public async Task<string?> GetAsync(CancellationToken ct)
        {
            if (cache.TryGetValue(CacheKey, out string? cached)) return cached;

            await BuildLock.WaitAsync(ct);
            try
            {
                if (cache.TryGetValue(CacheKey, out cached)) return cached;

                var overview = Fetch<MarketOverviewResponse>("market overview", marketData.GetMarketOverviewAsync);
                var macro = Fetch<MacroEconomyResponse>("macro economy", marketData.GetMacroEconomyAsync);
                var history = Fetch<MarketIndexHistoryResponse>("TAIEX history", marketData.GetMarketIndexHistoryAsync);
                var indicators = Fetch<IndicatorExplorerResponse>("indicators", marketData.GetIndicatorsAsync);
                await Task.WhenAll(overview, macro, history, indicators);

                var text = Render(
                    new MarketBackgroundData(overview.Result, macro.Result, history.Result, indicators.Result),
                    _time.GetUtcNow().ToOffset(TimeSpan.FromHours(8)));
                // Nothing at all is kept briefly, so a failing upstream is retried soon.
                cache.Set(CacheKey, text, text == null ? TimeSpan.FromMinutes(2) : Lifetime);
                return text;
            }
            finally
            {
                BuildLock.Release();
            }
        }

        private async Task<T?> Fetch<T>(string what, Func<CancellationToken, Task<Common.ServiceResult<T>>> get)
            where T : class
        {
            using var timeout = new CancellationTokenSource(SourceTimeout);
            try
            {
                var result = await get(timeout.Token);
                return result.IsSuccess ? result.Data : null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Chat market background: could not get the {Source}", what);
                return null;
            }
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>A number with at most <paramref name="decimals"/> places and nothing else.</summary>
        private static string N(decimal value, int decimals = 2) =>
            Math.Round(value, decimals).ToString("0.##########", Inv);

        /// <summary>A change: signed, so its direction is plain without a column for it.</summary>
        private static string S(decimal value, int decimals = 2) => (value > 0 ? "+" : "") + N(value, decimals);

        private static string Opt(decimal? value, Func<decimal, string> format) => value is { } v ? format(v) : "";

        /// <summary>A cell, quoted only when it would otherwise break the row.</summary>
        private static string Cell(string value) =>
            value.IndexOfAny([',', '"', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

        /// <summary>One table: <c>name[rows]{fields}:</c>, then a line per row.</summary>
        private static string Table(string name, string fields, IReadOnlyCollection<IEnumerable<string>> rows) =>
            $"{name}[{rows.Count}]{{{fields}}}:\n" +
            string.Join("\n", rows.Select(r => "  " + string.Join(",", r.Select(Cell))));

        /// <summary>A 民國 date like 1151020 as 10/20, or as it came when it is not one.</summary>
        private static string RocDate(string roc) =>
            roc.Length == 7 && int.TryParse(roc, out _) ? $"{roc[3..5]}/{roc[5..7]}" : roc;

        private static readonly (string Key, string Label)[] QuoteGroups =
            [("US", "美"), ("TW", "台"), ("FX", "匯"), ("Other", "其他")];

        /// <summary>The background as text, tables without data left out; null when there is none.</summary>
        internal static string? Render(MarketBackgroundData data, DateTimeOffset taipeiNow)
        {
            var tables = new List<string>();

            var quotes = data.Overview?.Quotes.Where(q => q.Price.HasValue).ToList() ?? [];
            if (quotes.Count > 0)
            {
                var rows = QuoteGroups
                    .SelectMany(g => quotes.Where(q => q.Group == g.Key).Select(q => (IEnumerable<string>)
                        [g.Label, q.Name, N(q.Price!.Value), Opt(q.Change, v => S(v)), Opt(q.ChangePercent, v => S(v))]))
                    .ToList();
                tables.Add(Table("報價", "區,項目,價,漲跌,漲跌%", rows));
            }

            tables.AddRange(TaiexTables(data.Macro, data.TaiexHistory));
            if (data.TaiexHistory != null) tables.AddRange(SeasonalTables(data.TaiexHistory));

            if (data.Macro?.SectorIndices is { Count: > 0 } sectors)
            {
                var rows = sectors.Select(s => (IEnumerable<string>)
                    [s.Name, N(s.CloseIndex), S((s.Direction.Contains('-') ? -1 : 1) * Math.Abs(s.ChangePercent))]).ToList();
                tables.Add(Table($"類股指數_{RocDate(sectors[0].Date)}", "類股,指數,漲跌%", rows));
            }

            if (data.Macro?.Margin is { } m)
            {
                tables.Add(Table("融資融券_上市_張", "融資餘額,融資增減,融券餘額,融券增減,券資比%",
                [[
                    N(m.MarginBalanceTotal, 0), Opt(m.MarginChange, v => S(v, 0)),
                    N(m.ShortBalanceTotal, 0), Opt(m.ShortChange, v => S(v, 0)),
                    m.MarginBalanceTotal > 0 ? N(m.ShortBalanceTotal * 100m / m.MarginBalanceTotal) : "",
                ]]));
            }

            if (data.Macro?.ExchangeRates is { Count: > 0 } rates)
            {
                var rows = rates.Select(r => (IEnumerable<string>)
                    [r.Currency, Opt(r.BuyRate, v => N(v, 4)), Opt(r.SellRate, v => N(v, 4))]).ToList();
                tables.Add(Table("台銀即期匯率", "幣別,買,賣", rows));
            }

            if (data.Macro?.BankRate is { } bank &&
                (bank.OneYearFixed.HasValue || bank.OneYearFloating.HasValue || bank.BaseLendingRate.HasValue))
            {
                tables.Add(Table($"利率_{bank.BankName}_{bank.Period}", "一年定存固定%,一年定存機動%,基準放款%",
                [[
                    Opt(bank.OneYearFixed, v => N(v, 3)), Opt(bank.OneYearFloating, v => N(v, 3)),
                    Opt(bank.BaseLendingRate, v => N(v, 3)),
                ]]));
            }

            var indicators = data.Indicators?.Indicators.Where(i => i.LatestValue.HasValue).ToList() ?? [];
            if (indicators.Count > 0)
            {
                var rows = indicators.Select(i => (IEnumerable<string>)
                    [i.Name, N(i.LatestValue!.Value), Opt(i.PreviousValue, v => N(v)), i.Unit, i.LatestDate ?? ""]).ToList();
                tables.Add(Table("美國總經_FRED", "指標,最新,前值,單位,日期", rows));
            }

            if (tables.Count == 0) return null;
            return $"# 市場背景（{taipeiNow:yyyy-MM-dd HH:mm} 台北時間的 /finance 頁面快照，可能落後即時）\n" +
                   "格式：表名[列數]{欄位}: 下接逗號分隔各列；漲跌帶正負號，%欄不帶%。" +
                   "引用時註明是背景資料及其日期；個股、即時或更細的資料用 tw-stock 工具查。\n" +
                   string.Join("\n", tables);
        }

        /// <summary>The TAIEX: the last close and its move from TWSE, then trend figures from history.</summary>
        private static IEnumerable<string> TaiexTables(MacroEconomyResponse? macro, MarketIndexHistoryResponse? history)
        {
            if (macro?.MarketIndex.LastOrDefault() is { } day)
            {
                var previous = day.TAIEX - day.Change;
                yield return Table("加權指數", "日期,收盤,漲跌,漲跌%,成交值億",
                [[
                    RocDate(day.Date), N(day.TAIEX), S(day.Change),
                    previous > 0 ? S(day.Change / previous * 100) : "", N(day.TradeValue / 100_000_000m, 0),
                ]]);
            }

            var points = history?.Points;
            if (points is not { Count: > 1 }) yield break;
            var last = points[^1];

            string ChangeSince(DateOnly from) =>
                points.LastOrDefault(p => p.Date <= from) is { Close: > 0 } b && points[0].Date <= from
                    ? S((last.Close / b.Close - 1) * 100)
                    : "";
            yield return Table($"加權指數報酬%_至{last.Date:MM/dd}", "1週,1月,3月,今年,1年",
            [[
                ChangeSince(last.Date.AddDays(-7)), ChangeSince(last.Date.AddMonths(-1)),
                ChangeSince(last.Date.AddMonths(-3)), ChangeSince(new DateOnly(last.Date.Year - 1, 12, 31)),
                ChangeSince(last.Date.AddYears(-1)),
            ]]);

            var year = points.Where(p => p.Date > last.Date.AddYears(-1)).ToList();
            var high = year.MaxBy(p => p.Close)!;
            var low = year.MinBy(p => p.Close)!;
            string Ma(int n) => points.Count >= n ? N(points.Skip(points.Count - n).Average(p => p.Close)) : "";
            yield return Table("加權指數位置", "收盤,52週高,高點日,52週低,低點日,距高%,MA20,MA60,MA240",
            [[
                N(last.Close), N(high.Close), $"{high.Date:yyyy/MM/dd}", N(low.Close), $"{low.Date:yyyy/MM/dd}",
                S((last.Close / high.Close - 1) * 100), Ma(20), Ma(60), Ma(240),
            ]]);
        }

        /// <summary>
        /// The seasonal card's reading: this year so far against the same span of earlier years, and
        /// how the current month went in those years.
        /// </summary>
        private static IEnumerable<string> SeasonalTables(MarketIndexHistoryResponse history)
        {
            var points = history.Points;
            if (points.Count < 2) yield break;
            var last = points[^1].Date;

            // Close on the last trading day on or before a date; no return before the series starts.
            decimal? CloseOn(DateOnly date) => points.LastOrDefault(p => p.Date <= date)?.Close;
            decimal? Return(DateOnly from, DateOnly to) =>
                points[0].Date <= from && CloseOn(from) is { } a && CloseOn(to) is { } b && a > 0 ? (b / a - 1) * 100 : null;
            DateOnly YearEnd(int y) => new(y, 12, 31);
            DateOnly MonthBefore(int y, int month) => new DateOnly(y, month, 1).AddDays(-1);

            var pastYears = Enumerable.Range(1, 10).Select(k => last.Year - k).ToList();

            var ytd = Return(YearEnd(last.Year - 1), last);
            var sameSpan = pastYears.Select(y => Return(YearEnd(y - 1), SameDay(y, last)))
                .Where(r => r.HasValue).Select(r => r!.Value).ToList();
            if (ytd is { } y0 && sameSpan.Count > 0)
            {
                yield return Table($"年初至{last:MM/dd}報酬%", "今年,過去平均,過去最低,過去最高,年數",
                    [[S(y0), S(sameSpan.Average()), S(sameSpan.Min()), S(sameSpan.Max()), sameSpan.Count.ToString(Inv)]]);
            }

            var month = pastYears
                .Select(y => Return(MonthBefore(y, last.Month), new DateOnly(y, last.Month, DateTime.DaysInMonth(y, last.Month))))
                .Where(r => r.HasValue).Select(r => r!.Value).ToList();
            if (month.Count > 0)
            {
                yield return Table($"{last.Month}月報酬%", "本月至今,過去平均,過去上漲年數,年數",
                [[
                    Opt(Return(MonthBefore(last.Year, last.Month), last), v => S(v)), S(month.Average()),
                    month.Count(r => r > 0).ToString(Inv), month.Count.ToString(Inv),
                ]]);
            }
        }

        /// <summary>The same month and day in another year (Feb 29 becomes Feb 28).</summary>
        private static DateOnly SameDay(int year, DateOnly date) =>
            new(year, date.Month, Math.Min(date.Day, DateTime.DaysInMonth(year, date.Month)));
    }
}
