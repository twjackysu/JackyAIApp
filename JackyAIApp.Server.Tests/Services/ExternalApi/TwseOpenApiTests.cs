using System.Net;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using JackyAIApp.Server.Tests.TestHelpers;
using JackyAIApp.Server.Tests.TestSupport;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.ExternalApi
{
    /// <summary>
    /// TWSE's OpenAPI as the rest of the app reads it (<see cref="TwseOpenApi"/>): each dataset parsed
    /// into typed rows that keep every field, downloaded once per Taipei day, and a dataset that could
    /// not be read is null — and tried again by the next caller. Fixtures only, no network calls.
    /// </summary>
    public class TwseOpenApiTests
    {
        private static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/TWSE", name));

        private readonly ManualTimeProvider _time = new();
        private readonly Mock<IHttpClientFactory> _factory = new();
        private readonly List<HttpRequestMessage> _requests = [];
        private Func<HttpRequestMessage, HttpResponseMessage> _respond;
        private readonly TwseOpenApi _twse;

        public TwseOpenApiTests()
        {
            _respond = request => StubHttpMessageHandler.JsonResponse(request.RequestUri!.ToString() switch
            {
                TwseOpenApi.DailyQuotesUrl => Fixture("stock_day_all.json"),
                TwseOpenApi.CompanyProfilesUrl => Fixture("company_list_t187ap03_l.json"),
                TwseOpenApi.MonthlyRevenuesUrl => Fixture("revenue_t187ap05_l.json"),
                var url => throw new InvalidOperationException($"Unexpected URL in test: {url}"),
            });
            _factory.Setup(f => f.CreateClient(TwseOpenApi.HttpClientName)).Returns(() => StubHttpMessageHandler.CreateClient(r =>
            {
                lock (_requests) _requests.Add(r);
                return _respond(r);
            }));
            _twse = new TwseOpenApi(_factory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<TwseOpenApi>.Instance, _time);
        }

        private void Answer(string json) => _respond = _ => StubHttpMessageHandler.JsonResponse(json);

        [Fact]
        public async Task DailyQuotes_HaveTheirCodeNameAndClose_AndEveryFieldInOrder()
        {
            var quote = (await _twse.GetDailyQuotesAsync())!.Single(q => q.Code == "2330");

            Assert.Equal(("台積電", 2440.00m), (quote.Name, quote.ClosingPrice));
            Assert.Equal(["Date", "Code", "Name", "TradeVolume", "TradeValue", "OpeningPrice", "HighestPrice", "LowestPrice", "ClosingPrice", "Change", "Transaction"],
                quote.Fields.Select(f => f.Key));
            Assert.Contains(KeyValuePair.Create("Change", "-20.0000"), quote.Fields);
        }

        [Fact]
        public async Task CompanyProfiles_HaveTheirCodeAndName()
        {
            var profiles = (await _twse.GetCompanyProfilesAsync())!;

            Assert.Equal(["2330", "1101"], profiles.Select(p => p.Code));
            Assert.Contains("台灣積體電路製造", profiles[0].Name);
            Assert.Contains(profiles[0].Fields, f => f.Key == "公司代號" && f.Value == "2330");
        }

        [Fact]
        public async Task MonthlyRevenues_HaveTheirMonthAndNumbers()
        {
            var revenue = (await _twse.GetMonthlyRevenuesAsync())!.Single(r => r.Code == "2330");

            Assert.Equal("11505", revenue.DataMonth);
            Assert.Equal(416975163m, revenue.Revenue);
            Assert.Equal(30.09498020271696m, revenue.RevenueYoY);
            Assert.Equal(1.5217099529812539m, revenue.RevenueMoM);
        }

        [Theory]
        [InlineData("1,050.00", 1050.00)]
        [InlineData(" 12.5 ", 12.5)]
        [InlineData("-", null)]
        [InlineData("N/A", null)]
        [InlineData("", null)]
        [InlineData("--", null)]
        public async Task Numbers_AreReadFromTwsesText(string text, double? expected)
        {
            Answer($$"""[{"Code":"2330","Name":"台積電","ClosingPrice":"{{text}}"}]""");

            var quote = (await _twse.GetDailyQuotesAsync())!.Single();

            Assert.Equal(expected is null ? null : (decimal)expected.Value, quote.ClosingPrice);
        }

        [Fact]
        public async Task RowsWithoutACode_AreLeftOut_AndCodesAreTrimmed()
        {
            Answer("""[{"Code":" 2330 ","Name":"台積電"},{"Code":"","Name":"?"},{"Name":"no code"},"not an object"]""");

            Assert.Equal(["2330"], (await _twse.GetDailyQuotesAsync())!.Select(q => q.Code));
        }

        [Fact]
        public async Task ADataset_IsDownloadedOnce_ForAllItsCallers_ThatDay()
        {
            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _twse.GetDailyQuotesAsync()));
            await _twse.GetDailyQuotesAsync();

            Assert.Single(_requests);
        }

        [Fact]
        public async Task ANewTaipeiDay_DownloadsItAgain()
        {
            // 2026-10-18 09:00 UTC is 17:00 in Taipei: seven hours to the next Taipei day.
            await _twse.GetDailyQuotesAsync();
            _time.Advance(TimeSpan.FromHours(6));
            await _twse.GetDailyQuotesAsync();
            Assert.Single(_requests);

            _time.Advance(TimeSpan.FromHours(1));
            await _twse.GetDailyQuotesAsync();

            Assert.Equal(2, _requests.Count);
        }

        [Fact]
        public async Task EachRequest_AsksForJson_WithItsOwnHeaders()
        {
            await _twse.GetDailyQuotesAsync();

            var request = Assert.Single(_requests);
            Assert.Equal(TwseOpenApi.DailyQuotesUrl, request.RequestUri!.ToString());
            Assert.Contains("application/json", request.Headers.Accept.ToString());
            Assert.NotEmpty(request.Headers.UserAgent);
        }

        public static TheoryData<Func<HttpRequestMessage, HttpResponseMessage>> Unreadable => new()
        {
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            _ => StubHttpMessageHandler.JsonResponse("""{"stat":"error"}"""),
            _ => StubHttpMessageHandler.JsonResponse("[]"),
            _ => StubHttpMessageHandler.JsonResponse("""[{"Name":"no code"}]"""),
            _ => StubHttpMessageHandler.JsonResponse("<html>Challenge</html>"),
            _ => throw new HttpRequestException("connection reset"),
        };

        [Theory]
        [MemberData(nameof(Unreadable))]
        public async Task AnUnreadableDataset_IsNull_AndTriedAgainNextTime(Func<HttpRequestMessage, HttpResponseMessage> answer)
        {
            var good = _respond;
            _respond = answer;

            Assert.Null(await _twse.GetDailyQuotesAsync());

            _respond = good;
            Assert.NotNull(await _twse.GetDailyQuotesAsync());
            Assert.Equal(2, _requests.Count);
        }
    }
}
