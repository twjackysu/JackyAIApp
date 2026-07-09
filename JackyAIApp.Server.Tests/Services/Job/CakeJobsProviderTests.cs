using JackyAIApp.Server.Services.Job.Providers;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Job
{
    /// <summary>
    /// Parses Cake's (cake.me) server-rendered __NEXT_DATA__ island — fixtures are raw .html
    /// captures rather than JSON, since this provider isn't a JSON API at all
    /// (THIRD_PARTY_CONTRACT_TESTS_PLAN.md section C2). "jobs_page.html" is trimmed down from a
    /// real page fetched at authoring time (search keyword 工程師), keeping enough of the
    /// original entries to exercise a normal entry, a null-salary entry, a non-engineering title
    /// (verifies the title filter), and a bare-minimum entry (verifies the field fallbacks).
    /// </summary>
    public class CakeJobsProviderTests
    {
        private const string FixtureDir = "Fixtures/Cake";

        private static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, FixtureDir, name));

        private static CakeJobsProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var httpClient = StubHttpMessageHandler.CreateClient(responder);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            return new CakeJobsProvider(httpClientFactory.Object, NullLogger<CakeJobsProvider>.Instance);
        }

        private static HttpResponseMessage HtmlResponse(string html) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") };

        [Fact]
        public async Task FetchAsync_NormalPage_ParsesEngineeringJobsAndExcludesNonEngineeringTitles()
        {
            // Fixture's single page has 4 entries; the provider treats "< 10 entries" as the last
            // page, so it stops after page 1 without requesting page 2.
            var provider = CreateProvider(_ => HtmlResponse(ReadFixture("jobs_page.html")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            // "業務經理 Sales Manager" must be filtered out by the title include/exclude lists.
            Assert.DoesNotContain(jobs, j => j.Title.Contains("業務經理"));

            var normal = Assert.Single(jobs, j => j.Id == "cake-hour-loop-software-engineer-f3c235");
            Assert.Equal("軟體工程師 Software Engineer (Full-stack)", normal.Title);
            Assert.Equal("Hour Loop 飛輪電商", normal.Company);
            Assert.Contains("台北市", normal.Location);
            Assert.Equal("TWD 800,000 - 1,200,000/年", normal.SalaryRange);
            Assert.Equal("https://www.cake.me/companies/hour-loop/jobs/software-engineer-f3c235", normal.Url);
            Assert.Equal("Cake", normal.Platform);
        }

        [Fact]
        public async Task FetchAsync_NullSalaryFields_FormatsWithoutThrowing()
        {
            var provider = CreateProvider(_ => HtmlResponse(ReadFixture("jobs_page.html")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            var nullSalary = Assert.Single(jobs, j => j.Id == "cake-jkopay-full-service-engineer-dc4e7e");
            // salary.min/max are both null in the fixture -> FormatSalary should return null, not throw.
            Assert.Null(nullSalary.SalaryRange);
        }

        [Fact]
        public async Task FetchAsync_MinimalEntryMissingPageLocationsSalaryTags_UsesSafeDefaults()
        {
            var provider = CreateProvider(_ => HtmlResponse(ReadFixture("jobs_page.html")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            var minimal = Assert.Single(jobs, j => j.Title.Contains("minimal fixture"));
            Assert.Equal("Cake", minimal.Company); // no "page" object -> falls back to default company name
            Assert.Equal("台灣", minimal.Location); // no "locations" array -> default
            Assert.Null(minimal.SalaryRange);       // no "salary" object
            Assert.Empty(minimal.Tags);
            Assert.Null(minimal.Description);
            Assert.Equal("https://www.cake.me/jobs", minimal.Url); // no page.path/path pair -> fallback URL
        }

        [Fact]
        public async Task FetchAsync_NextDataScriptMissing_ReturnsEmptyListInsteadOfThrowing()
        {
            // Simulates Cake removing/renaming the __NEXT_DATA__ island entirely — the single
            // highest-value failure mode this provider has (see class doc + JobBoardContractTests).
            var provider = CreateProvider(_ => HtmlResponse(ReadFixture("jobs_page_no_next_data.html")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            Assert.Empty(jobs);
        }

        [Fact]
        public async Task FetchAsync_NextDataPresentButEntityByPathIdPathChanged_ReturnsEmptyListInsteadOfThrowing()
        {
            // Simulates Cake restructuring initialState.jobSearch's internal shape (e.g. renaming
            // entityByPathId) while still emitting *a* __NEXT_DATA__ script — TryGetJobMap should
            // fail closed rather than throw.
            var provider = CreateProvider(_ => HtmlResponse(ReadFixture("jobs_page_restructured.html")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            Assert.Empty(jobs);
        }

        [Fact]
        public async Task FetchAsync_NonSuccessStatusCode_ReturnsEmptyListInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));

            var jobs = await provider.FetchAsync("工程師", "AND");

            Assert.Empty(jobs);
        }

        [Fact]
        public async Task FetchAsync_NetworkException_BubblesUpAsIsExpectedByAggregator()
        {
            // Same contract as Job104Provider: only per-item parse errors are caught locally, a
            // transport-level failure propagates for JobAggregatorService's FetchSafe to isolate.
            var provider = CreateProvider(_ => throw new HttpRequestException("network down"));

            await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchAsync("工程師", "AND"));
        }
    }
}
