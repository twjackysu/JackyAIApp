using JackyAIApp.Server.Services.Job.Providers;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Job
{
    /// <summary>
    /// Parses response shapes from 104's unofficial job search JSON API — fixtures only, no
    /// network calls (THIRD_PARTY_CONTRACT_TESTS_PLAN.md section C1).
    ///
    /// The fixtures here are hand-built to match the field names <see cref="Job104Provider"/>
    /// actually reads (jobNo/jobName/custName/jobAddrNoDesc/salaryType/salaryLow/salaryHigh/
    /// appearDate/link.job/pcSkills[].description/descSnippet), rather than a live capture: at
    /// authoring time 104's API was behind a Cloudflare "Just a moment..." JS challenge (HTTP 403)
    /// for every request this environment sent it, curl and a bare .NET HttpClient alike, even
    /// with the exact Referer/User-Agent headers the "104" named HttpClient sends. See
    /// JobBoardContractTests.cs for the live half of this story.
    /// </summary>
    public class Job104ProviderTests
    {
        private const string FixtureDir = "Fixtures/Job104";

        private static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, FixtureDir, name));

        private static Job104Provider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var httpClient = StubHttpMessageHandler.CreateClient(responder);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            return new Job104Provider(httpClientFactory.Object, NullLogger<Job104Provider>.Instance);
        }

        [Fact]
        public async Task FetchAsync_V2ArrayFormat_ParsesRequiredAndOptionalFields()
        {
            // page 1 has fewer than 30 jobs (2), so the provider stops after page 1.
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(ReadFixture("search_page1.json")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            Assert.Equal(2, jobs.Count);

            var first = jobs[0];
            Assert.Equal("104-abc1234567890123abcd", first.Id);
            Assert.Equal("後端工程師 Backend Engineer", first.Title);
            Assert.Equal("測試科技股份有限公司", first.Company);
            Assert.Equal("台北市信義區", first.Location); // prefers jobAddrNoDesc over jobAddress
            Assert.Equal("月薪 50,000 - 80,000 元", first.SalaryRange);
            Assert.Equal("https://www.104.com.tw/job/abc123", first.Url);
            Assert.Equal(new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc), first.PostedAt);
            Assert.Equal("104人力銀行", first.Platform);
            Assert.Equal(["C#", ".NET", "SQL"], first.Tags);
            Assert.Contains("C#", first.Description);
        }

        [Fact]
        public async Task FetchAsync_SalaryTypeNegotiable_ReturnsNegotiableLabelRegardlessOfLowHigh()
        {
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(ReadFixture("search_page1.json")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            var second = jobs[1];
            Assert.Equal("面議", second.SalaryRange); // salaryType 10, even though salaryHigh is the 9,999,999 sentinel
            Assert.Equal("新北市板橋區", second.Location); // no jobAddrNoDesc -> falls back to jobAddress
            Assert.Empty(second.Tags);
            Assert.Null(second.Description);
        }

        [Fact]
        public async Task FetchAsync_LegacyDataListFormat_StillParses()
        {
            // The plan flags this as a "legacy" shape (data.list[] instead of data[] directly);
            // if this fixture is what's actually live again, that's itself a signal 104 reverted
            // its API — see the comment on JobBoardContractTests's shape-detection assertion.
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(ReadFixture("search_legacy_format.json")));

            var jobs = await provider.FetchAsync("資料工程師", "AND");

            var job = Assert.Single(jobs);
            Assert.Equal("104-leg1234567890123abcd", job.Id);
            Assert.Equal("資料工程師 Data Engineer (legacy shape)", job.Title);
            Assert.Equal("舊版格式測試公司", job.Company);
        }

        [Fact]
        public async Task FetchAsync_OnlyRequiredFieldsPresent_DoesNotThrowAndFillsSafeDefaults()
        {
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(ReadFixture("search_missing_optional_fields.json")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            var job = Assert.Single(jobs);
            Assert.Equal("104-min0000000000000abcd", job.Id);
            Assert.Equal("工程師（最小欄位）", job.Title);
            Assert.Equal("最小欄位測試公司", job.Company);
            Assert.Equal("", job.Location);
            Assert.Equal("面議", job.SalaryRange); // no salaryLow/salaryHigh/salaryType at all
            Assert.Equal("https://www.104.com.tw/job/min0000000000000abcd", job.Url); // falls back to synthesized URL (jobNo, not the "104-" prefixed Id)
            Assert.Empty(job.Tags);
            Assert.Null(job.Description);
        }

        [Fact]
        public async Task FetchAsync_ResponseHasNoDataField_ReturnsEmptyListInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => StubHttpMessageHandler.JsonResponse(ReadFixture("search_no_data_field.json")));

            var jobs = await provider.FetchAsync("工程師", "AND");

            Assert.Empty(jobs);
        }

        [Fact]
        public async Task FetchAsync_NonSuccessStatusCode_ReturnsEmptyListInsteadOfThrowing()
        {
            var provider = CreateProvider(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));

            var jobs = await provider.FetchAsync("工程師", "AND");

            Assert.Empty(jobs);
        }

        [Fact]
        public async Task FetchAsync_NetworkException_BubblesUpAsIsExpectedByAggregator()
        {
            // Unlike DividendService/TWSE providers, Job104Provider does not itself catch
            // top-level network exceptions (only per-item parse errors) — the aggregator layer is
            // responsible for isolating one provider's failure from the others. Documenting that
            // contract here so a future refactor that changes it is a deliberate choice, not an
            // accident.
            var provider = CreateProvider(_ => throw new HttpRequestException("network down"));

            await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchAsync("工程師", "AND"));
        }
    }
}
