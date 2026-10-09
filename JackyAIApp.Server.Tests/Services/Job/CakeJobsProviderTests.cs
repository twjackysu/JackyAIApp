using System.Text.Json;
using JackyAIApp.Server.Services.Job.Providers;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Job
{
    /// <summary>
    /// Parses Cake's (cake.me) client search API — POST api.cake.me/api/client/v2/jobs/search
    /// (THIRD_PARTY_CONTRACT_TESTS_PLAN.md section C2). "jobs_search.json" mirrors the live
    /// response shape, keeping enough entries to exercise a normal entry, a null-salary entry, an
    /// aggregated external posting (different shape: id/source_url/external_company), a
    /// non-engineering title (verifies the title filter), and a bare-minimum entry (verifies the
    /// field fallbacks).
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

        [Fact]
        public async Task FetchAsync_NormalPage_ParsesEngineeringJobsAndExcludesNonEngineeringTitles()
        {
            // Fixture reports total_pages = 1, so the provider stops after page 1.
            var requests = 0;
            var provider = CreateProvider(_ => { requests++; return StubHttpMessageHandler.JsonResponse(ReadFixture("jobs_search.json")); });

            var jobs = await provider.FetchAsync("工程師", "AND");

            Assert.Equal(1, requests);
            // "業務經理 Sales Manager" must be filtered out by the title include/exclude lists.
            Assert.DoesNotContain(jobs, j => j.Title.Contains("業務經理"));

            var normal = Assert.Single(jobs, j => j.Id == "cake-hour-loop-software-engineer-f3c235");
            Assert.Equal("軟體工程師 Software Engineer (Full-stack)", normal.Title);
            Assert.Equal("Hour Loop 飛輪電商", normal.Company);
            Assert.Contains("台北市", normal.Location);
            Assert.Equal("TWD 800,000 - 1,200,000/年", normal.SalaryRange);
            Assert.Equal("https://www.cake.me/companies/hour-loop/jobs/software-engineer-f3c235", normal.Url);
            Assert.Contains("entry_level", normal.Tags);
            Assert.Equal("Cake", normal.Platform);
        }

        [Fact]
        public async Task FetchAsync_SendsSearchRequestWithKeywordTaiwanFilterAndSortBy()
        {
            HttpRequestMessage? captured = null;
            string? body = null;
            var provider = CreateProvider(req =>
            {
                captured = req;
                body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return StubHttpMessageHandler.JsonResponse(ReadFixture("jobs_search.json"));
            });

            await provider.FetchAsync("  工程師  ", "AND");

            Assert.NotNull(captured);
            Assert.Equal(HttpMethod.Post, captured!.Method);
            Assert.Equal("https://api.cake.me/api/client/v2/jobs/search", captured.RequestUri!.ToString());

            using var doc = JsonDocument.Parse(body!);
            var root = doc.RootElement;
            Assert.Equal("工程師", root.GetProperty("query").GetString());
            Assert.Equal("Taiwan", root.GetProperty("filters").GetProperty("locations")[0].GetString());
            // The API rejects requests without sort_by (422).
            Assert.False(string.IsNullOrEmpty(root.GetProperty("sort_by").GetString()));
            Assert.Equal(1, root.GetProperty("page").GetInt32());
        }

        [Fact]
        public async Task FetchAsync_ExternalPosting_UsesSourceUrlAndExternalCompany()
        {
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(ReadFixture("jobs_search.json")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            var external = Assert.Single(jobs, j => j.Id == "cake-ext-98f47fc1-abd3-4fdc-9815-7631b41c9556");
            Assert.Equal("Qualcomm", external.Company);
            Assert.StartsWith("https://careers.qualcomm.com/", external.Url);
            Assert.Equal("新竹市, 台灣", external.Location);
            Assert.Null(external.SalaryRange); // salary: null
            Assert.Equal(["Hardware Engineering"], external.Tags);
        }

        [Fact]
        public async Task FetchAsync_NullSalaryFields_FormatsWithoutThrowing()
        {
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(ReadFixture("jobs_search.json")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            var nullSalary = Assert.Single(jobs, j => j.Id == "cake-jkopay-full-service-engineer-dc4e7e");
            // salary.min/max are both null in the fixture -> FormatSalary should return null, not throw.
            Assert.Null(nullSalary.SalaryRange);
        }

        [Fact]
        public async Task FetchAsync_MinimalEntryMissingPageLocationsSalaryTags_UsesSafeDefaults()
        {
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(ReadFixture("jobs_search.json")));

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
        public async Task FetchAsync_MultiplePages_FollowsTotalPagesAndDedupes()
        {
            // Every page returns the same 30 entries and claims 5 pages: the provider must cap at
            // its own page limit and de-duplicate the repeated entries.
            var entry = JsonDocument.Parse(ReadFixture("jobs_search.json")).RootElement.GetProperty("data")[0].GetRawText();
            var page = $"{{\"total_entries\":150,\"total_pages\":5,\"per_page\":30,\"current_page\":1,\"data\":[{string.Join(",", Enumerable.Repeat(entry, 30))}]}}";
            var requests = 0;
            var provider = CreateProvider(_ => { requests++; return StubHttpMessageHandler.JsonResponse(page); });

            var jobs = await provider.FetchAsync("工程師", "AND");

            Assert.Equal(2, requests);
            Assert.Single(jobs);
        }

        [Fact]
        public async Task FetchAsync_DataArrayMissing_ReturnsEmptyListInsteadOfThrowing()
        {
            // Simulates Cake restructuring the response envelope (e.g. renaming "data").
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(ReadFixture("jobs_search_restructured.json")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            Assert.Empty(jobs);
        }

        [Fact]
        public async Task FetchAsync_NonJsonBody_ReturnsEmptyListInsteadOfThrowing()
        {
            // e.g. a Cloudflare "Just a moment..." challenge page served with 200.
            var provider = CreateProvider(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<!DOCTYPE html><html><head><title>Just a moment...</title></head></html>",
                    System.Text.Encoding.UTF8, "text/html")
            });

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
