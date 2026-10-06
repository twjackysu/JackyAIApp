using System.Net;
using System.Text;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.Portfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// The TW stock-search list: filled from TWSE + TPEx OpenAPI dumps by a background refresh,
    /// searched from memory. Replaces a refresh that ran inside the search request against
    /// isin.twse.com.tw, timed out at 30s every time, and so made every search take 30s.
    /// </summary>
    public class TwListedCompanyServiceTests : IDisposable
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 6, 6, 0, 0, TimeSpan.Zero);

        private readonly DbContextOptions<AzureSQLDBContext> _options;
        private readonly AzureSQLDBContext _db;
        private readonly RoutingHandler _http = new();
        private readonly TwListedCompanyService _service;

        public TwListedCompanyServiceTests()
        {
            _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _db = new AzureSQLDBContext(_options);

            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient(_http, disposeHandler: false));

            _service = new TwListedCompanyService(
                new TestDbContextFactory(_options),
                httpFactory.Object,
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<TwListedCompanyService>.Instance,
                new FakeTime(Now));
        }

        public void Dispose() => _db.Dispose();

        private static string TwseJson(params (string Code, string Name)[] rows) =>
            "[" + string.Join(",", rows.Select(r =>
                $"{{\"Date\":\"1151005\",\"Code\":\"{r.Code}\",\"Name\":\"{r.Name}\",\"ClosingPrice\":\"10.00\"}}")) + "]";

        private static string TpexJson(params (string Code, string Name)[] rows) =>
            "[" + string.Join(",", rows.Select(r =>
                $"{{\"Date\":\"1151005\",\"SecuritiesCompanyCode\":\"{r.Code}\",\"CompanyName\":\"{r.Name}\",\"Close\":\"10.00\"}}")) + "]";

        private void ServeLists(string twse, string tpex)
        {
            _http.Respond(TwListedCompanyService.TwseUrl, HttpStatusCode.OK, twse);
            _http.Respond(TwListedCompanyService.TpexUrl, HttpStatusCode.OK, tpex);
        }

        private async Task SeedAsync(params TwListedCompany[] rows)
        {
            _db.TwListedCompanies.AddRange(rows);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }

        private async Task<Dictionary<string, TwListedCompany>> StoredAsync()
        {
            await using var db = new AzureSQLDBContext(_options);
            return await db.TwListedCompanies.ToDictionaryAsync(c => c.Symbol);
        }

        // ── Refresh ─────────────────────────────────────────────────────────

        [Fact]
        public async Task RefreshAsync_StoresBothExchangesWithoutWarrants()
        {
            ServeLists(
                TwseJson(("2330", "台積電"), ("00981A", "主動統一台股增長")),
                TpexJson(("6488", "環球晶"), ("00679B", "元大美債20年"), ("700019", "宏捷科統一5C購01"), ("72414U", "振曜群益5A售01")));

            Assert.True(await _service.RefreshAsync());

            var stored = await StoredAsync();
            Assert.Equal(new[] { "00679B", "00981A", "2330", "6488" }, stored.Keys.Order(StringComparer.Ordinal));
            Assert.Equal("環球晶", stored["6488"].Name);
            Assert.All(stored.Values, c => Assert.Equal(Now.UtcDateTime, c.LastRefreshedAt));
        }

        [Fact]
        public async Task RefreshAsync_RenamesInPlace_KeepsBrieflyUnseen_PrunesLongUnseen()
        {
            await SeedAsync(
                new TwListedCompany { Symbol = "2330", Name = "台積電舊名", LastRefreshedAt = Now.UtcDateTime.AddDays(-1) },
                // Suspended this week: missing from today's dumps, but must stay searchable.
                new TwListedCompany { Symbol = "1234", Name = "停牌中", LastRefreshedAt = Now.UtcDateTime.AddDays(-5) },
                // Gone for longer than any suspension: delisted.
                new TwListedCompany { Symbol = "9999", Name = "已下市", LastRefreshedAt = Now.UtcDateTime.AddDays(-31) });
            ServeLists(TwseJson(("2330", "台積電")), TpexJson(("6488", "環球晶")));

            Assert.True(await _service.RefreshAsync());

            var stored = await StoredAsync();
            Assert.Equal(new[] { "1234", "2330", "6488" }, stored.Keys.Order(StringComparer.Ordinal));
            Assert.Equal("台積電", stored["2330"].Name);
            Assert.Equal(Now.UtcDateTime.AddDays(-5), stored["1234"].LastRefreshedAt);
        }

        public static TheoryData<HttpStatusCode, string, HttpStatusCode, string> BrokenSources => new()
        {
            { HttpStatusCode.InternalServerError, "", HttpStatusCode.OK, TpexJson(("6488", "環球晶")) },
            { HttpStatusCode.OK, TwseJson(("2330", "台積電")), HttpStatusCode.ServiceUnavailable, "" },
            { HttpStatusCode.OK, TwseJson(("2330", "台積電")), HttpStatusCode.OK, "[]" },
            { HttpStatusCode.OK, "{\"message\":\"maintenance\"}", HttpStatusCode.OK, TpexJson(("6488", "環球晶")) },
            { HttpStatusCode.OK, "<html>維護中</html>", HttpStatusCode.OK, TpexJson(("6488", "環球晶")) },
        };

        [Theory]
        [MemberData(nameof(BrokenSources))]
        public async Task RefreshAsync_EitherExchangeBroken_WritesNothing(
            HttpStatusCode twseStatus, string twseBody, HttpStatusCode tpexStatus, string tpexBody)
        {
            // An old row that a successful refresh would prune: it must survive a failed one.
            await SeedAsync(new TwListedCompany { Symbol = "9999", Name = "舊資料", LastRefreshedAt = Now.UtcDateTime.AddDays(-40) });
            _http.Respond(TwListedCompanyService.TwseUrl, twseStatus, twseBody);
            _http.Respond(TwListedCompanyService.TpexUrl, tpexStatus, tpexBody);

            Assert.False(await _service.RefreshAsync());

            var stored = await StoredAsync();
            Assert.Equal(new[] { "9999" }, stored.Keys);
        }

        [Fact]
        public async Task RefreshAsync_NetworkFailure_ReturnsFalseInsteadOfThrowing()
        {
            _http.Throw(TwListedCompanyService.TpexUrl, new HttpRequestException("connection reset"));
            _http.Respond(TwListedCompanyService.TwseUrl, HttpStatusCode.OK, TwseJson(("2330", "台積電")));

            Assert.False(await _service.RefreshAsync());
            Assert.Empty(await StoredAsync());
        }

        [Theory]
        [InlineData("700019", true)]  // TPEx 認購
        [InlineData("72414U", true)]  // TPEx 認售
        [InlineData("030001", true)]  // TWSE 認購
        [InlineData("08123P", true)]  // TWSE 認售
        [InlineData("2330", false)]
        [InlineData("00981A", false)] // ETF
        [InlineData("006201", false)] // ETF
        [InlineData("01001T", false)] // REIT
        [InlineData("020000", false)] // ETN
        [InlineData("910322", false)] // DR
        [InlineData("2887Z1", false)] // preferred
        [InlineData("1101B", false)]  // preferred
        public void IsWarrant(string symbol, bool expected) =>
            Assert.Equal(expected, TwListedCompanyService.IsWarrant(symbol));

        // ── Search ──────────────────────────────────────────────────────────

        [Fact]
        public async Task SearchAsync_NeverFetchesUpstream_EvenWhenTheListIsOld()
        {
            // The reported bug: an old list made the search request itself refresh, and the
            // refresh's 30s timeout was what every search waited on.
            await SeedAsync(new TwListedCompany { Symbol = "2330", Name = "台積電", LastRefreshedAt = Now.UtcDateTime.AddDays(-60) });

            var result = await _service.SearchAsync("2330");

            Assert.Equal("2330", Assert.Single(result).Symbol);
            Assert.Equal(0, _http.Calls);
        }

        [Fact]
        public async Task SearchAsync_SymbolPrefixMatchesFirst_ThenNameMatches()
        {
            await SeedAsync(
                new TwListedCompany { Symbol = "2330", Name = "台積電" },
                new TwListedCompany { Symbol = "5871", Name = "中租-KY" },
                new TwListedCompany { Symbol = "00981A", Name = "主動統一台股增長" },
                new TwListedCompany { Symbol = "1101", Name = "台泥" });

            Assert.Equal("00981A", Assert.Single(await _service.SearchAsync("00981a")).Symbol);
            Assert.Equal("5871", Assert.Single(await _service.SearchAsync("ky")).Symbol);
            Assert.Equal(new[] { "2330" }, (await _service.SearchAsync("台積")).Select(r => r.Symbol));
            // "台" matches two names; with no symbol match they come back in symbol order.
            Assert.Equal(new[] { "00981A", "1101", "2330" }, (await _service.SearchAsync("台")).Select(r => r.Symbol));
            Assert.All(await _service.SearchAsync("台"), r => Assert.Equal("TW", r.Market));
        }

        [Fact]
        public async Task SearchAsync_SymbolMatchOutranksNameMatch()
        {
            await SeedAsync(
                new TwListedCompany { Symbol = "1101", Name = "台泥" },
                new TwListedCompany { Symbol = "9910", Name = "豐泰1101" });

            Assert.Equal(new[] { "1101", "9910" }, (await _service.SearchAsync("1101")).Select(r => r.Symbol));
        }

        [Fact]
        public async Task SearchAsync_SeesARefreshImmediately()
        {
            await SeedAsync(new TwListedCompany { Symbol = "2330", Name = "台積電" });
            Assert.Empty(await _service.SearchAsync("6488")); // the list is now cached

            ServeLists(TwseJson(("2330", "台積電")), TpexJson(("6488", "環球晶")));
            Assert.True(await _service.RefreshAsync());

            Assert.Equal("6488", Assert.Single(await _service.SearchAsync("6488")).Symbol);
        }

        [Fact]
        public async Task SearchAsync_EmptyListIsNotCached()
        {
            // Right after the table is first created, search must not keep answering "nothing"
            // for an hour once the first refresh has filled it.
            Assert.Empty(await _service.SearchAsync("2330"));

            await SeedAsync(new TwListedCompany { Symbol = "2330", Name = "台積電" });

            Assert.Single(await _service.SearchAsync("2330"));
        }

        [Fact]
        public async Task SearchAsync_BlankKeyword_ReturnsNothing()
        {
            await SeedAsync(new TwListedCompany { Symbol = "2330", Name = "台積電" });
            Assert.Empty(await _service.SearchAsync("  "));
        }

        [Fact]
        public async Task GetLastRefreshedAtAsync_IsTheNewestRow()
        {
            Assert.Null(await _service.GetLastRefreshedAtAsync());

            await SeedAsync(
                new TwListedCompany { Symbol = "2330", Name = "台積電", LastRefreshedAt = Now.UtcDateTime.AddDays(-40) },
                new TwListedCompany { Symbol = "6488", Name = "環球晶", LastRefreshedAt = Now.UtcDateTime.AddHours(-3) });

            Assert.Equal(Now.UtcDateTime.AddHours(-3), await _service.GetLastRefreshedAtAsync());
        }

        private sealed class FakeTime(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        /// <summary>Answers by URL, and counts calls so a test can prove nothing went upstream.</summary>
        private sealed class RoutingHandler : HttpMessageHandler
        {
            private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = [];
            private int _calls;

            public int Calls => _calls;

            public void Respond(string url, HttpStatusCode status, string body) =>
                _routes[url] = () => new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };

            public void Throw(string url, Exception ex) => _routes[url] = () => throw ex;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Interlocked.Increment(ref _calls);
                var url = request.RequestUri!.ToString();
                return _routes.TryGetValue(url, out var respond)
                    ? Task.FromResult(respond())
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }
    }
}
