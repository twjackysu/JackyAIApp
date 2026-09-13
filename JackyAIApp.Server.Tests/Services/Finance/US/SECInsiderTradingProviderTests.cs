using System.Net;
using JackyAIApp.Server.Services.Finance.DataProviders.US;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance.US
{
    /// <summary>
    /// Parses a real Form 4 ownership table captured from SEC EDGAR's own-disp HTML endpoint —
    /// fixture only, no network calls. THIRD_PARTY_CONTRACT_TESTS_PLAN.md section B8. This is
    /// explicitly the most fragile integration in the plan (HTML structure via HtmlAgilityPack,
    /// not a JSON API contract).
    /// </summary>
    public class SECInsiderTradingProviderTests
    {
        private const string FixtureDir = "Fixtures/SEC";
        private const string Cik = "0000320193"; // AAPL

        private static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, FixtureDir, name));

        private static SECInsiderTradingProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var httpClient = StubHttpMessageHandler.CreateClient(responder);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new SECInsiderTradingProvider(httpClientFactory.Object, cache, NullLogger<SECInsiderTradingProvider>.Instance);
        }

        private static HttpResponseMessage HtmlResponse(string html, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(html) };

        [Fact]
        public async Task FetchAsync_ParsesTransactionReportTable()
        {
            // FetchAsync filters to the last 90 days relative to DateTime.UtcNow, so the fixture's
            // dates are placeholders substituted with dates relative to "now" here — otherwise the
            // hardcoded dates would age out of the window and this test would decay over time.
            var html = ReadFixture("insider_transactions_aapl.html")
                .Replace("{{RECENT_DATE}}", DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd"))
                .Replace("{{OLDER_DATE}}", DateTime.UtcNow.AddDays(-31).ToString("yyyy-MM-dd"));
            var provider = CreateProvider(_ => HtmlResponse(html));

            var result = await provider.FetchAsync(Cik, "AAPL");

            Assert.NotNull(result);
            Assert.Equal("AAPL", result!.StockCode);
            // Fixture has 6 data rows: S-Sale, M-Exempt, M-Exempt, F-InKind, F-InKind, M-Exempt.
            // F-InKind isn't in the "PSMAD" code set ExtractTransactionCode recognizes, so those
            // 2 rows are dropped — 4 valid transactions remain (1 sale, 3 exercises).
            Assert.Equal(4, result.RecentTransactions.Count);
            Assert.Equal(1, result.SaleCount);
            Assert.Equal(0, result.PurchaseCount); // no "P-Purchase" rows in this window
            Assert.Contains(result.RecentTransactions, t => t.TransactionCode == "S" && t.OwnerName == "Borders Ben");
            Assert.Contains(result.RecentTransactions, t => t.TransactionCode == "M");
            Assert.All(result.RecentTransactions, t => Assert.True(t.Shares > 0));
        }

        [Fact]
        public async Task FetchAsync_MissingTransactionReportTable_ReturnsNullInsteadOfThrowing()
        {
            const string html = "<html><body><p>No filings found for this issuer.</p></body></html>";
            var provider = CreateProvider(_ => HtmlResponse(html));

            var result = await provider.FetchAsync(Cik, "AAPL");

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchAsync_RowsWithTooFewCells_AreSkippedInsteadOfThrowing()
        {
            const string html = """
                <table id="transaction-report">
                <tr class="header"><th>A/D</th><th>Date</th><th>Deemed</th><th>Owner</th><th>Form</th><th>Type</th><th>D/I</th><th>Shares</th><th>Owned</th></tr>
                <tr><td>A</td><td>2026-01-01</td><td>&nbsp;</td><td>Too Few Cells</td></tr>
                </table>
                """;
            var provider = CreateProvider(_ => HtmlResponse(html));

            var result = await provider.FetchAsync(Cik, "AAPL");

            // The only data row has 4 cells (< 9 required) so it's skipped, leaving zero
            // transactions parsed — provider treats that the same as "no data" (null).
            Assert.Null(result);
        }

        [Fact]
        public async Task FetchAsync_HttpFailure_ReturnsNullInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var result = await provider.FetchAsync(Cik, "AAPL");

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchAsync_NetworkFailure_ReturnsNullInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => throw new HttpRequestException("network down"));

            var result = await provider.FetchAsync(Cik, "AAPL");

            Assert.Null(result);
        }
    }
}
