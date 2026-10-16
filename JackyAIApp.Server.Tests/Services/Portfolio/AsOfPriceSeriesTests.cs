using System.Diagnostics;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Portfolio;
using Xunit.Abstractions;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// AsOfPriceSeries replaced a per-cell <c>Where(day &lt;= D).OrderByDescending(date).FirstOrDefault()</c>
    /// in PortfolioService.ValueAsOfDate and GetBenchmarkComparisonAsync. It must return what that
    /// LINQ returned for every input — including same-day duplicates — so the old expressions live
    /// below as the oracle and the tests compare against them rather than against re-derived numbers.
    /// </summary>
    public class AsOfPriceSeriesTests(ITestOutputHelper output)
    {
        // ── Oracles: the exact expressions that were in PortfolioService before ──────────────────

        private static decimal? LegacyStock(IEnumerable<DailyPrice> prices, DateOnly date)
        {
            var dt = date.ToDateTime(TimeOnly.MinValue);
            return prices
                .Where(x => x.Date.Date <= dt)
                .OrderByDescending(x => x.Date)
                .FirstOrDefault()?.Close;
        }

        private static decimal? LegacyFund(IEnumerable<PricePoint> prices, DateOnly date)
        {
            var dayKey = date.ToString("yyyy-MM-dd");
            return prices
                .Where(x => string.CompareOrdinal(x.Date, dayKey) <= 0)
                .OrderByDescending(x => x.Date, StringComparer.Ordinal)
                .FirstOrDefault()?.Close;
        }

        private static DailyPrice Bar(string dateTime, decimal close) =>
            new() { Date = DateTime.Parse(dateTime, System.Globalization.CultureInfo.InvariantCulture), Close = close };

        private static PricePoint Nav(string date, decimal close) => new() { Date = date, Close = close };

        private static string Key(DateOnly d) => d.ToString("yyyy-MM-dd");

        // ── Stock ───────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Stock_BeforeTheFirstEntry_ReturnsNull()
        {
            var series = AsOfPriceSeries.ForStock([Bar("2026-07-21", 10m), Bar("2026-07-22", 11m)]);

            Assert.Null(series.CloseOnOrBefore(new DateOnly(2026, 7, 20)));
        }

        [Fact]
        public void Stock_ExactlyOnAnEntry_ReturnsThatEntry()
        {
            var series = AsOfPriceSeries.ForStock([Bar("2026-07-21", 10m), Bar("2026-07-22", 11m), Bar("2026-07-23", 12m)]);

            Assert.Equal(10m, series.CloseOnOrBefore(new DateOnly(2026, 7, 21)));
            Assert.Equal(11m, series.CloseOnOrBefore(new DateOnly(2026, 7, 22)));
            Assert.Equal(12m, series.CloseOnOrBefore(new DateOnly(2026, 7, 23)));
        }

        [Fact]
        public void Stock_BetweenEntriesAcrossAWeekend_ReturnsTheEarlierEntry()
        {
            // Fri 2026-07-24 then Tue 2026-07-28 (Mon a holiday): Sat, Sun and Mon all see Friday.
            var series = AsOfPriceSeries.ForStock([Bar("2026-07-24", 50m), Bar("2026-07-28", 60m)]);

            Assert.Equal(50m, series.CloseOnOrBefore(new DateOnly(2026, 7, 25)));
            Assert.Equal(50m, series.CloseOnOrBefore(new DateOnly(2026, 7, 26)));
            Assert.Equal(50m, series.CloseOnOrBefore(new DateOnly(2026, 7, 27)));
            Assert.Equal(60m, series.CloseOnOrBefore(new DateOnly(2026, 7, 28)));
        }

        [Fact]
        public void Stock_AfterTheLastEntry_ReturnsTheLastEntry()
        {
            var series = AsOfPriceSeries.ForStock([Bar("2026-07-21", 10m), Bar("2026-07-22", 11m)]);

            Assert.Equal(11m, series.CloseOnOrBefore(new DateOnly(2026, 8, 30)));
        }

        [Fact]
        public void Stock_UnsortedInput_GivesTheSameAnswersAsSortedInput()
        {
            var sorted = new[] { Bar("2026-07-20", 1m), Bar("2026-07-21", 2m), Bar("2026-07-22", 3m), Bar("2026-07-23", 4m) };
            var shuffled = new[] { sorted[2], sorted[0], sorted[3], sorted[1] };

            var a = AsOfPriceSeries.ForStock(sorted);
            var b = AsOfPriceSeries.ForStock(shuffled);

            for (var d = new DateOnly(2026, 7, 18); d <= new DateOnly(2026, 7, 26); d = d.AddDays(1))
                Assert.Equal(a.CloseOnOrBefore(d), b.CloseOnOrBefore(d));
            Assert.Equal(3m, b.CloseOnOrBefore(new DateOnly(2026, 7, 22)));
        }

        [Fact]
        public void Stock_EmptyHistory_ReturnsNull()
        {
            var series = AsOfPriceSeries.ForStock([]);

            Assert.Equal(0, series.Count);
            Assert.Null(series.CloseOnOrBefore(new DateOnly(2026, 7, 20)));
        }

        [Fact]
        public void Stock_AnEntryWithZeroCloseIsStillAnEntry()
        {
            // "No price" (null) and "a price of 0" are different outcomes to the callers, so a
            // zero close must come back as 0 rather than being skipped in favour of an earlier bar.
            var series = AsOfPriceSeries.ForStock([Bar("2026-07-21", 10m), Bar("2026-07-22", 0m)]);

            Assert.Equal(0m, series.CloseOnOrBefore(new DateOnly(2026, 7, 22)));
        }

        [Fact]
        public void Stock_SameDayWithDifferentTimes_TheLaterTimestampWins_InEitherSourceOrder()
        {
            // Yahoo stamps bars with a time of day, so one calendar day can carry two entries.
            // The old expression ordered by the full timestamp, so the later one wins.
            var morning = Bar("2026-07-22 13:30", 100m);
            var evening = Bar("2026-07-22 20:00", 101m);

            foreach (var source in new[] { new[] { morning, evening }, new[] { evening, morning } })
            {
                var series = AsOfPriceSeries.ForStock(source);
                Assert.Equal(101m, series.CloseOnOrBefore(new DateOnly(2026, 7, 22)));
                Assert.Equal(101m, series.CloseOnOrBefore(new DateOnly(2026, 7, 23)));
                Assert.Null(series.CloseOnOrBefore(new DateOnly(2026, 7, 21)));
            }
        }

        [Fact]
        public void Stock_IdenticalTimestamps_TheEntryThatCameFirstInTheSourceWins()
        {
            // LINQ's OrderByDescending is stable, so for an exact tie the old expression returned
            // the first one in the source. (GetNetWorthHistoryCoreAsync's cursor would pick the last;
            // the two are left apart on purpose — see AsOfPriceSeries.)
            var first = Bar("2026-07-22", 7m);
            var second = Bar("2026-07-22", 8m);

            Assert.Equal(7m, AsOfPriceSeries.ForStock([first, second]).CloseOnOrBefore(new DateOnly(2026, 7, 22)));
            Assert.Equal(8m, AsOfPriceSeries.ForStock([second, first]).CloseOnOrBefore(new DateOnly(2026, 7, 22)));
        }

        [Fact]
        public void Stock_DoesNotReorderOrChangeTheSourceList()
        {
            // The source is a cached, shared instance (see PortfolioService.GetNetWorthHistoryCoreAsync).
            var source = new List<DailyPrice> { Bar("2026-07-23", 3m), Bar("2026-07-21", 1m), Bar("2026-07-22", 2m) };
            var before = source.ToArray();

            _ = AsOfPriceSeries.ForStock(source);

            Assert.Equal(before.Length, source.Count);
            for (var i = 0; i < before.Length; i++)
                Assert.Same(before[i], source[i]);
        }

        // ── Fund (ISO date strings, ordinal) ────────────────────────────────────────────────────

        [Fact]
        public void Fund_BeforeExactlyBetweenAfter()
        {
            var series = AsOfPriceSeries.ForFund([Nav("2026-07-21", 10m), Nav("2026-07-24", 12m)]);

            Assert.Null(series.CloseOnOrBefore("2026-07-20"));
            Assert.Equal(10m, series.CloseOnOrBefore("2026-07-21"));
            Assert.Equal(10m, series.CloseOnOrBefore("2026-07-23"));
            Assert.Equal(12m, series.CloseOnOrBefore("2026-07-24"));
            Assert.Equal(12m, series.CloseOnOrBefore("2026-12-31"));
        }

        [Fact]
        public void Fund_UnsortedInputAndEmpty()
        {
            var series = AsOfPriceSeries.ForFund([Nav("2026-07-24", 12m), Nav("2026-07-21", 10m), Nav("2026-07-22", 11m)]);

            Assert.Equal(11m, series.CloseOnOrBefore("2026-07-23"));
            Assert.Null(AsOfPriceSeries.ForFund([]).CloseOnOrBefore("2026-07-23"));
        }

        [Fact]
        public void Fund_SameDateTwice_TheEntryThatCameFirstInTheSourceWins()
        {
            // FundDataService sorts with List.Sort (not stable) and does not de-duplicate, so which
            // duplicate arrives first is not something this class can improve on; it only has to
            // keep the old "first in the source" rule for whatever order it is handed.
            Assert.Equal(1m, AsOfPriceSeries.ForFund([Nav("2026-07-22", 1m), Nav("2026-07-22", 2m)]).CloseOnOrBefore("2026-07-22"));
            Assert.Equal(2m, AsOfPriceSeries.ForFund([Nav("2026-07-22", 2m), Nav("2026-07-22", 1m)]).CloseOnOrBefore("2026-07-22"));
        }

        [Fact]
        public void Fund_DoesNotReorderOrChangeTheSourceList()
        {
            var source = new List<PricePoint> { Nav("2026-07-23", 3m), Nav("2026-07-21", 1m), Nav("2026-07-22", 2m) };
            var before = source.ToArray();

            _ = AsOfPriceSeries.ForFund(source);

            for (var i = 0; i < before.Length; i++)
                Assert.Same(before[i], source[i]);
        }

        // ── Book ────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Book_IndexesEachSymbolAndKeepsTheFundCurrency_AbsentSymbolsStayAbsent()
        {
            var stocks = new Dictionary<string, MarketData>
            {
                ["2330"] = new() { HistoricalPrices = [Bar("2026-07-21", 10m)] },
            };
            var funds = new Dictionary<string, FundNavHistory>
            {
                ["ALI006"] = new("USD", [Nav("2026-07-21", 5m)]),
            };

            var book = AsOfPriceBook.From(stocks, funds);

            Assert.Equal(10m, book.Stocks["2330"].CloseOnOrBefore(new DateOnly(2026, 7, 22)));
            Assert.False(book.Stocks.ContainsKey("0050"));
            Assert.Equal("USD", book.Funds["ALI006"].Currency);
            Assert.Equal(5m, book.Funds["ALI006"].Prices.CloseOnOrBefore("2026-07-22"));
            Assert.False(book.Funds.ContainsKey("OTHER"));
        }

        // ── New vs. old implementation, same inputs ─────────────────────────────────────────────

        [Fact]
        public void RandomHistories_NewAndOldImplementationsAgreeOnEveryQueryDay()
        {
            var checkedQueries = 0;
            for (var seed = 1; seed <= 60; seed++)
            {
                var rng = new Random(seed);
                var origin = new DateOnly(2026, 1, 1);

                // Stocks: random day gaps, repeated days, sometimes a time of day, some zero closes,
                // shuffled so the source order is arbitrary.
                var stockBars = new List<DailyPrice>();
                var day = origin;
                for (var i = rng.Next(0, 80); i > 0; i--)
                {
                    day = day.AddDays(rng.Next(0, 5)); // 0 = same day as the previous bar
                    var time = rng.Next(3) == 0 ? new TimeOnly(rng.Next(0, 24), rng.Next(0, 60)) : TimeOnly.MinValue;
                    stockBars.Add(new DailyPrice
                    {
                        Date = day.ToDateTime(time),
                        Close = rng.Next(0, 10) == 0 ? 0m : rng.Next(1, 500),
                    });
                }
                stockBars = [.. stockBars.OrderBy(_ => rng.Next())];
                var stockEnd = day;

                // Funds: same shape with date strings (no time of day).
                var fundNavs = new List<PricePoint>();
                day = origin;
                for (var i = rng.Next(0, 80); i > 0; i--)
                {
                    day = day.AddDays(rng.Next(0, 5));
                    fundNavs.Add(new PricePoint { Date = Key(day), Close = rng.Next(0, 10) == 0 ? 0m : rng.Next(1, 500) });
                }
                fundNavs = [.. fundNavs.OrderBy(_ => rng.Next())];

                var stockSeries = AsOfPriceSeries.ForStock(stockBars);
                var fundSeries = AsOfPriceSeries.ForFund(fundNavs);

                // From well before the first bar to well past the last.
                var queryEnd = (day > stockEnd ? day : stockEnd).AddDays(10);
                for (var q = origin.AddDays(-10); q <= queryEnd; q = q.AddDays(1))
                {
                    Assert.Equal(LegacyStock(stockBars, q), stockSeries.CloseOnOrBefore(q));
                    Assert.Equal(LegacyFund(fundNavs, q), fundSeries.CloseOnOrBefore(Key(q)));
                    checkedQueries++;
                }
            }

            Assert.True(checkedQueries > 1000);
        }

        [Fact]
        public void ThreeYearsByThirtyHoldings_AgreesWithTheOldImplementation_AndReportsRoughTimings()
        {
            // Sizing from the task: 3 years of daily valuation (the drawdown / benchmark upper bound)
            // over 30 holdings, each with ~750 price rows. Timings are printed for the PR description
            // only; nothing here asserts on them (they would be flaky) — the assertion is that both
            // implementations produce the same total.
            const int holdings = 30, historyRows = 750, days = 1095;
            var end = new DateOnly(2026, 10, 16);
            var start = end.AddDays(-(days - 1));
            var rng = new Random(54);

            var histories = Enumerable.Range(0, holdings).Select(_ =>
            {
                var d = end.AddDays(-(historyRows * 7 / 5)); // ~750 weekdays back
                var rows = new List<DailyPrice>();
                while (d <= end)
                {
                    if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                        rows.Add(new DailyPrice { Date = d.ToDateTime(TimeOnly.MinValue), Close = rng.Next(10, 1000) });
                    d = d.AddDays(1);
                }
                return rows;
            }).ToList();

            var swOld = Stopwatch.StartNew();
            decimal oldTotal = 0;
            for (var date = start; date <= end; date = date.AddDays(1))
                foreach (var history in histories)
                    oldTotal += LegacyStock(history, date) ?? 0;
            swOld.Stop();

            var swNew = Stopwatch.StartNew();
            var series = histories.Select(h => AsOfPriceSeries.ForStock(h)).ToList();
            decimal newTotal = 0;
            for (var date = start; date <= end; date = date.AddDays(1))
                foreach (var s in series)
                    newTotal += s.CloseOnOrBefore(date) ?? 0;
            swNew.Stop();

            output.WriteLine(
                $"{holdings} holdings x {historyRows} rows x {days} days = {holdings * days:N0} lookups: " +
                $"old {swOld.ElapsedMilliseconds} ms, new (sort once + binary search, build included) {swNew.ElapsedMilliseconds} ms");

            Assert.Equal(oldTotal, newTotal);
        }
    }
}
