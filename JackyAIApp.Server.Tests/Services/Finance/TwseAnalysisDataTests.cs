using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// The text the stock AI analysis is given (<see cref="TwseAnalysisData.DescribeAsync"/>):
    /// each TWSE dataset's row for the stock, field by field, under its section heading. Fixtures
    /// only, no network calls.
    /// </summary>
    public class TwseAnalysisDataTests
    {
        private static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/TWSE", name));

        private readonly ConcurrentDictionary<string, int> _requests = new();
        private readonly HashSet<string> _failing = [];
        private string? _indices;

        private HttpResponseMessage Route(HttpRequestMessage request)
        {
            var url = request.RequestUri!.ToString();
            var endpoint = url.Split('/').Last().Split('?')[0];
            _requests.AddOrUpdate(endpoint, 1, (_, n) => n + 1);
            if (_failing.Contains(endpoint)) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            return StubHttpMessageHandler.JsonResponse(endpoint switch
            {
                "STOCK_DAY_ALL" => Fixture("stock_day_all.json"),
                "t187ap03_L" => Fixture("company_list_t187ap03_l.json"),
                "t187ap05_L" => Fixture("revenue_t187ap05_l.json"),
                "MI_INDEX" => _indices ?? Fixture("mi_index.json"),
                "STOCK_DAY_AVG_ALL" => """[{"Date":"1151021","Code":"2330","Name":"台積電","ClosingPrice":"1050.00","MonthlyAveragePrice":"1032.15"}]""",
                "BWIBBU_ALL" => """[{"Code":"2330","Name":"台積電","PEratio":"32.80","DividendYield":"0.90","PBratio":"10.74"}]""",
                "t187ap45_L" => """[{"公司代號":"2330","公司名稱":"台積電","股利年度":"114","股東配發-盈餘分配之現金股利(元/股)":"4.5"}]""",
                "t187ap06_L_ci" => """[{"公司代號":"2330","公司名稱":"台積電","營業收入":"839253664","本期淨利（淨損）":"361564103"}]""",
                "t187ap07_L_ci" => """[{"公司代號":"2330","公司名稱":"台積電","資產總額":"7254658906","負債總額":"2283195106"}]""",
                _ => throw new InvalidOperationException($"Unexpected URL in test: {url}"),
            });
        }

        private TwseAnalysisData CreateService()
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => StubHttpMessageHandler.CreateClient(Route));
            return new TwseAnalysisData(
                new TwseOpenApi(factory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<TwseOpenApi>.Instance, TimeProvider.System),
                NullLogger<TwseAnalysisData>.Instance);
        }

        /// <summary>The lines under one section heading, up to the blank line that ends it.</summary>
        private static List<string> Section(string text, string title)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var start = Array.IndexOf(lines, $"--- {title} ---");
            if (start < 0) return [];
            return lines.Skip(start + 1).TakeWhile(l => l.Length > 0).ToList();
        }

        /// <summary>The fixture's row for the stock, as "field: value" lines in the order TWSE sent them.</summary>
        private static List<string> RowLines(string fixture, string codeField, string code)
        {
            using var doc = JsonDocument.Parse(Fixture(fixture));
            var row = doc.RootElement.EnumerateArray().First(r => r.GetProperty(codeField).GetString() == code);
            return row.EnumerateObject().Select(p => $"{p.Name}: {p.Value.GetString()}").ToList();
        }

        [Fact]
        public async Task EachDatasetsRowForTheStock_IsListedFieldByField_UnderItsSection()
        {
            var text = await CreateService().DescribeAsync("2330");

            Assert.Equal(RowLines("stock_day_all.json", "Code", "2330"), Section(text, "Daily Trading Data (Technical)"));
            Assert.Equal(RowLines("company_list_t187ap03_l.json", "公司代號", "2330"), Section(text, "Company Profile"));
            Assert.Equal(RowLines("revenue_t187ap05_l.json", "公司代號", "2330"), Section(text, "Monthly Revenue (Fundamental)"));
            Assert.Contains("PEratio: 32.80", Section(text, "Valuation Ratios (P/E, P/B, Dividend Yield)"));
            Assert.StartsWith("=== Comprehensive Stock Analysis for 2330 ===", text);
        }

        [Fact]
        public async Task AStockMissingFromADataset_SaysSo_InThatSection()
        {
            var text = await CreateService().DescribeAsync("9999");

            Assert.Equal(["No data found for stock code: 9999"], Section(text, "Daily Trading Data (Technical)"));
            Assert.Equal(["No data found for stock code: 9999"], Section(text, "Company Profile"));
            Assert.Equal(["No data found for stock code: 9999"], Section(text, "Monthly Revenue (Fundamental)"));
        }

        [Fact]
        public async Task ADatasetThatFails_LeavesItsSectionOut_AndTheRestIn()
        {
            _failing.Add("STOCK_DAY_ALL");
            _failing.Add("t187ap05_L");

            var text = await CreateService().DescribeAsync("2330");

            Assert.DoesNotContain("--- Daily Trading Data (Technical) ---", text);
            Assert.DoesNotContain("--- Monthly Revenue (Fundamental) ---", text);
            Assert.NotEmpty(Section(text, "Company Profile"));
        }

        [Fact]
        public async Task EverythingFailing_LeavesOnlyTheHeadings()
        {
            foreach (var e in new[] { "STOCK_DAY_ALL", "t187ap03_L", "t187ap05_L", "MI_INDEX", "STOCK_DAY_AVG_ALL", "BWIBBU_ALL", "t187ap45_L", "t187ap06_L_ci", "t187ap07_L_ci" })
                _failing.Add(e);

            var text = await CreateService().DescribeAsync("2330");

            // The timeframe headings alone are past the 200 characters below which the service
            // answers "Limited data available", so that note is never what comes back.
            Assert.StartsWith("=== Comprehensive Stock Analysis for 2330 ===", text);
            Assert.DoesNotContain("--- ", text);
        }

        [Fact]
        public async Task ADataset_IsDownloadedOnce_ForAnyNumberOfStocks()
        {
            var service = CreateService();

            await service.DescribeAsync("2330");
            await service.DescribeAsync("1101");
            await service.DescribeAsync("9999");

            Assert.All(_requests.Values, count => Assert.Equal(1, count));
            Assert.Equal(9, _requests.Count);
        }

        [Fact]
        public async Task AFailedDownload_IsTriedAgain_NextTime()
        {
            var service = CreateService();
            _failing.Add("t187ap03_L");
            Assert.Empty(Section(await service.DescribeAsync("2330"), "Company Profile"));

            _failing.Clear();
            var text = await service.DescribeAsync("2330");

            Assert.Contains("公司代號: 2330", Section(text, "Company Profile"));
            Assert.Equal(2, _requests["t187ap03_L"]);
        }

        [Fact]
        public async Task TheMarketIndices_AreListedWholeRowByRow_UpToTheirBudget()
        {
            var rows = Enumerable.Range(1, 30).Select(i => $$"""{"日期":"1151021","指數":"指數{{i}}","收盤指數":"{{1000 + i}}.00"}""");
            _indices = "[" + string.Join(",", rows) + "]";

            var section = Section(await CreateService().DescribeAsync("2330"), "Market Index & Institutional Activity (Chip)");

            Assert.Equal(["日期: 1151021", "指數: 指數1", "收盤指數: 1001.00", "日期: 1151021", "指數: 指數2"], section.Take(5));
            Assert.Equal("...", section[^1]);
            Assert.True(section.Count % 3 == 1, "only whole rows, then the ellipsis");
            Assert.InRange(section.Sum(l => l.Length + Environment.NewLine.Length), 900, 1010);
        }
    }
}
