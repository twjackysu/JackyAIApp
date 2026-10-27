using System.Reflection;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.DTO.Job;
using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services.Job;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Job
{
    /// <summary>
    /// The anonymous job search is bounded (D-165): per address, a search's length, and one cache entry per real search
    /// whatever the caller writes for the platforms.
    /// </summary>
    public class JobSearchLimitsTests
    {
        private static Mock<IJobProvider> Provider(string id)
        {
            var provider = new Mock<IJobProvider>();
            provider.SetupGet(p => p.PlatformId).Returns(id);
            provider.Setup(p => p.FetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            return provider;
        }

        [Fact]
        public async Task PlatformsTheSiteDoesNotHave_DoNotMakeANewSearch()
        {
            var p104 = Provider("104");
            var cake = Provider("cake");
            var cache = new MemoryCache(new MemoryCacheOptions());
            var service = new JobAggregatorService([p104.Object, cake.Object], cache, NullLogger<JobAggregatorService>.Instance);

            await service.SearchAsync(new JobSearchRequest("dotnet", "AND", ["104", "cake"]));
            await service.SearchAsync(new JobSearchRequest("dotnet", "AND", ["cake", "104", "x1"]));
            await service.SearchAsync(new JobSearchRequest("dotnet", "AND", ["104", "CAKE", "x2"]));

            p104.Verify(p => p.FetchAsync("dotnet", "AND", It.IsAny<CancellationToken>()), Times.Once);
            cake.Verify(p => p.FetchAsync("dotnet", "AND", It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void TheSearch_IsHeldPerAddress()
        {
            var visitor = new DefaultHttpContext();
            visitor.Request.Headers[RateLimitPolicies.CloudflareClientIpHeader] = "203.0.113.7";
            var other = new DefaultHttpContext();
            other.Request.Headers[RateLimitPolicies.CloudflareClientIpHeader] = "198.51.100.2";
            var partition = RateLimitPolicies.JobSearchPartition(visitor);
            Assert.NotEqual(partition.PartitionKey, RateLimitPolicies.JobSearchPartition(other).PartitionKey);
            using (var limiter = partition.Factory(partition.PartitionKey))
            {
                for (var i = 0; i < RateLimitPolicies.JobSearchPermitsPerMinute; i++)
                    Assert.True(limiter.AttemptAcquire().IsAcquired, $"search {i + 1}");
                Assert.False(limiter.AttemptAcquire().IsAcquired);
            }

            Assert.Equal(RateLimitPolicies.JobSearch,
                typeof(JobController).GetMethod(nameof(JobController.GetListings))!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
        }

        [Theory]
        [InlineData("https://careers.example.com/job/1", "https://careers.example.com/job/1")]
        [InlineData("http://careers.example.com/job/1", null)]
        [InlineData("javascript:alert(1)", null)]
        [InlineData("/jobs/1", null)]
        [InlineData(null, null)]
        public void ACakeExternalJobsLink_IsKeptOnlyWhenHttps(string? sourceUrl, string? kept)
        {
            Assert.Equal(kept, JackyAIApp.Server.Services.Job.Providers.CakeJobsProvider.HttpsUrl(sourceUrl));
        }
    }
}
