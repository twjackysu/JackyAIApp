using System.Globalization;
using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.Finance.DataProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Daily OHLC bars for Stock Intelligence's trendline chart (<c>GET api/finance/price-bars</c>):
    /// the TAIEX's cached six years with their daily range, and a stock's cached history cleaned up.
    /// The trendlines themselves are found client-side.
    /// </summary>
    public class FinanceQueryServicePriceBarsTests
    {
        private readonly Mock<IMarketIndexHistoryProvider> _index = new();
        private readonly Mock<IMarketDataProviderFactory> _factory = new();
        private readonly Mock<IMarketDataProvider> _provider = new();
        private readonly Mock<IStockCodeResolver> _resolver = new();

        public FinanceQueryServicePriceBarsTests()
        {
            _factory.Setup(f => f.GetMarketDataProvider(It.IsAny<MarketRegion>())).Returns(_provider.Object);
            _factory.Setup(f => f.DetectRegion(It.IsAny<string>()))
                .Returns((string code) => code.All(char.IsAsciiLetter) ? MarketRegion.US : MarketRegion.TW);
        }

        // Only the dependencies price bars use are real; the rest are never touched.
        private FinanceQueryService Service() => new(
            NullLogger<FinanceQueryService>.Instance, null!, null!, _resolver.Object, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, _index.Object, null!, null!, null!, null!,
            _factory.Object, null!);

        // ---- TAIEX ---------------------------------------------------------------------------

        [Fact]
        public async Task Taiex_ReturnsTheWholeCachedSeries_WithEachDaysRange()
        {
            var asOf = new DateOnly(2026, 10, 15);
            var points = new List<MarketIndexPoint>();
            for (var d = asOf.AddYears(-6); d <= asOf; d = d.AddDays(1))
                points.Add(new MarketIndexPoint(d, 20000m) { Open = 19900m, High = 20100m, Low = 19800m });
            // A day the source gave no usable range for.
            points[^2] = new MarketIndexPoint(asOf.AddDays(-1), 21000m);
            _index.Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketIndexHistoryResponse("^TWII", "加權指數", "Yahoo Finance", asOf, points));

            var result = await Service().GetPriceBarsAsync("taiex", null, default);

            var bars = Assert.IsType<PriceBarsResponse>(result.Data);
            Assert.Equal("TAIEX", bars.Code);
            Assert.Equal("加權指數", bars.Name);
            Assert.Equal(asOf, bars.AsOf);
            Assert.Equal(asOf.AddYears(-6), bars.Bars[0].Date);   // the chart picks its own start
            Assert.Equal(asOf, bars.Bars[^1].Date);
            Assert.Equal(new PriceBar(asOf, 19900m, 20100m, 19800m, 20000m), bars.Bars[^1]);
            Assert.Equal(new PriceBar(asOf.AddDays(-1), 21000m, 21000m, 21000m, 21000m), bars.Bars[^2]);   // flat at the close
        }

        [Fact]
        public async Task Taiex_Unavailable_IsAnExternalError()
        {
            _index.Setup(p => p.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((MarketIndexHistoryResponse?)null);

            var result = await Service().GetPriceBarsAsync("TAIEX", null, default);

            Assert.Equal(ErrorCodes.ExternalApiError, result.Error);
        }

        // ---- Stocks --------------------------------------------------------------------------

        private static DailyPrice Day(DateTime date, decimal close, decimal? high = null) => new()
        {
            Date = date,
            Open = close - 1,
            High = high ?? close + 2,
            Low = close - 2,
            Close = close,
        };

        private static List<DailyPrice> Days(int count, DateTime start) =>
            Enumerable.Range(0, count).Select(i => Day(start.AddDays(i), 500 + i)).ToList();

        [Fact]
        public async Task TwStock_ResolvesTheCode_SortsDedupesAndDropsBadDays()
        {
            var start = new DateTime(2026, 4, 1);
            var prices = Days(40, start);
            prices.Reverse();                                                    // provider order is not trusted
            prices.Add(Day(start.AddDays(5).AddHours(5), 999));                  // same date again: the later one wins
            prices.Add(new DailyPrice { Date = start.AddDays(50), Close = 510 }); // no range: dropped
            _resolver.Setup(r => r.ResolveStockCodeAsync("台積電", It.IsAny<CancellationToken>())).ReturnsAsync("2330");
            _provider.Setup(p => p.FetchAsync("2330", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData { StockCode = "2330", CompanyName = "台積電", HistoricalPrices = prices });

            var result = await Service().GetPriceBarsAsync("台積電", null, default);

            var bars = Assert.IsType<PriceBarsResponse>(result.Data);
            Assert.Equal("2330", bars.Code);
            Assert.Equal("台積電", bars.Name);
            Assert.Equal(40, bars.Bars.Count);
            Assert.Equal(bars.Bars.OrderBy(b => b.Date), bars.Bars);
            Assert.Equal(999m, bars.Bars.Single(b => b.Date == DateOnly.FromDateTime(start.AddDays(5))).Close);
            Assert.Equal(DateOnly.FromDateTime(start.AddDays(39)), bars.AsOf);
        }

        [Fact]
        public async Task UsStock_UsesTheCodeAsIs()
        {
            _provider.Setup(p => p.FetchAsync("AAPL", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData { StockCode = "AAPL", HistoricalPrices = Days(35, new DateTime(2026, 5, 1)) });

            var result = await Service().GetPriceBarsAsync(" aapl ", "US", default);

            var bars = Assert.IsType<PriceBarsResponse>(result.Data);
            Assert.Equal("AAPL", bars.Code);
            Assert.Equal("AAPL", bars.Name);   // no company name: falls back to the code
            _factory.Verify(f => f.GetMarketDataProvider(MarketRegion.US));
            _resolver.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task TooLittleHistory_IsNotFound()
        {
            _resolver.Setup(r => r.ResolveStockCodeAsync("6999", It.IsAny<CancellationToken>())).ReturnsAsync("6999");
            _provider.Setup(p => p.FetchAsync("6999", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData { HistoricalPrices = Days(FinanceQueryService.MinPriceBars - 1, new DateTime(2026, 9, 1)) });

            var result = await Service().GetPriceBarsAsync("6999", null, default);

            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task UnresolvableTwCode_IsNotFound()
        {
            _resolver.Setup(r => r.ResolveStockCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("");

            var result = await Service().GetPriceBarsAsync("不存在的公司", null, default);

            Assert.Equal(ErrorCodes.NotFound, result.Error);
            _provider.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ProviderFailure_IsAnExternalError()
        {
            _provider.Setup(p => p.FetchAsync("MSFT", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

            var result = await Service().GetPriceBarsAsync("MSFT", null, default);

            Assert.Equal(ErrorCodes.ExternalApiError, result.Error);
        }

        [Fact]
        public async Task BlankCode_IsABadRequest()
        {
            var result = await Service().GetPriceBarsAsync("  ", null, default);

            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        // ---- TAIEX range parsing ---------------------------------------------------------------

        /// <summary>Yahoo chart JSON for weekdays ending on <paramref name="last"/>, with open/high/low arrays.</summary>
        private static string ChartWithRanges(DateOnly last, int count, Func<int, (double O, double H, double L, double C)> bar)
        {
            var days = new List<DateOnly>();
            for (var d = last; days.Count < count; d = d.AddDays(-1))
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days.Add(d);
            days.Reverse();
            string Join(Func<(double O, double H, double L, double C), double> pick) =>
                string.Join(",", days.Select((_, i) => pick(bar(i)).ToString("R", CultureInfo.InvariantCulture)));
            var stamps = string.Join(",", days.Select(d =>
                new DateTimeOffset(d.Year, d.Month, d.Day, 1, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()));
            return "{\"chart\":{\"result\":[{\"timestamp\":[" + stamps + "],\"indicators\":{\"quote\":[{" +
                   $"\"open\":[{Join(b => b.O)}],\"high\":[{Join(b => b.H)}],\"low\":[{Join(b => b.L)}],\"close\":[{Join(b => b.C)}]" +
                   "}]}}],\"error\":null}}";
        }

        private static readonly DateOnly Thu = new(2026, 10, 8);
        private static readonly DateTimeOffset ThuEvening = new DateTimeOffset(2026, 10, 8, 18, 0, 0, TimeSpan.FromHours(8)).ToUniversalTime();

        [Fact]
        public void Parse_KeepsEachDaysRange_AndDropsAnInconsistentOne()
        {
            var json = ChartWithRanges(Thu, 1450, i => i == 1449
                ? (20000, 19990, 19900, 20000)                       // high below the close: not a real range
                : (20000 + i, 20050.123 + i, 19950 + i, 20010 + i));

            var (history, rejection) = MarketIndexHistoryProvider.Parse(json, ThuEvening);

            Assert.Null(rejection);
            var first = history!.Points[0];
            Assert.Equal((20000m, 20050.12m, 19950m, 20010m), (first.Open, first.High, first.Low, first.Close));
            var last = history.Points[^1];
            Assert.Equal(20000m, last.Close);   // the close still counts
            Assert.Null(last.Open);
            Assert.Null(last.High);
            Assert.Null(last.Low);
        }

        [Fact]
        public void MarketIndexHistoryJson_StillCarriesOnlyDateAndClose()
        {
            var point = new MarketIndexPoint(Thu, 20000m) { Open = 1m, High = 2m, Low = 0.5m };

            var json = JsonSerializer.Serialize(point, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.Equal("{\"date\":\"2026-10-08\",\"close\":20000}", json);
        }
    }
}
