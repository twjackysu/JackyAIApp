using System.Net;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using DotnetSdkUtilities.Factory.ResponseFactory;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading.RateLimiting;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// The anonymous session upload keeps rows in process memory for two hours, so every way in is
    /// bounded: shape and estimated size per upload, a process-wide byte budget, a check before the
    /// body is read, and a rate limit that treats all anonymous callers as one.
    /// </summary>
    public class SessionUploadLimitsTests
    {
        private static List<Dictionary<string, string>> Rows(int count, int columns = 3, int cellLength = 4) =>
            Enumerable.Range(0, count)
                .Select(_ => Enumerable.Range(0, columns)
                    .ToDictionary(c => $"Col{c}", _ => new string('x', cellLength)))
                .ToList();

        private static (SessionDataStore Store, SyncEvictingMemoryCache Cache, SessionUploadBudget Budget) Store(
            SessionUploadOptions? limits = null)
        {
            var options = Options.Create(limits ?? new SessionUploadOptions());
            var cache = new SyncEvictingMemoryCache();
            var budget = new SessionUploadBudget(options);
            return (new SessionDataStore(cache, options, budget), cache, budget);
        }

        private static string SessionId(ServiceResult<SessionUploadResponseDto> result) =>
            result.Data!.SessionId;

        // ── Shape and size of one upload ─────────────────────────────────────

        [Fact]
        public void Upload_MoreColumnsThanTheLimit_IsRefusedAndNotStored()
        {
            var (store, cache, budget) = Store();

            var result = store.Upload(new SessionUploadDto { Rows = Rows(2, columns: 201) });

            Assert.Equal(ErrorCodes.BadRequest, result.Error);
            Assert.Contains("200", result.ErrorMessage);
            Assert.Equal(0, cache.Count);
            Assert.Equal(0, budget.UsedBytes);
        }

        [Fact]
        public void Upload_ColumnsSpreadAcrossRows_CountTogether()
        {
            var (store, _, _) = Store(new SessionUploadOptions { MaxColumns = 3 });
            var rows = new List<Dictionary<string, string>>
            {
                new() { ["A"] = "1", ["B"] = "2" },
                new() { ["C"] = "3", ["D"] = "4" },
            };

            Assert.Equal(ErrorCodes.BadRequest, store.Upload(new SessionUploadDto { Rows = rows }).Error);
        }

        [Fact]
        public void Upload_CellOverTheLimit_IsRefused()
        {
            var (store, cache, _) = Store();

            var result = store.Upload(new SessionUploadDto { Rows = Rows(2, cellLength: 1_001) });

            Assert.Equal(ErrorCodes.BadRequest, result.Error);
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Upload_ColumnNameOverTheLimit_IsRefused()
        {
            var (store, _, _) = Store();
            var rows = new List<Dictionary<string, string>> { new() { [new string('k', 1_001)] = "v" } };

            Assert.Equal(ErrorCodes.BadRequest, store.Upload(new SessionUploadDto { Rows = rows }).Error);
        }

        [Fact]
        public void Upload_NullRow_IsRefused()
        {
            var (store, _, _) = Store();
            var rows = new List<Dictionary<string, string>> { new() { ["A"] = "1" }, null! };

            Assert.Equal(ErrorCodes.BadRequest, store.Upload(new SessionUploadDto { Rows = rows }).Error);
        }

        [Fact]
        public void Upload_EstimatedSizeOverTheSessionCap_Is413AndNotStored()
        {
            var rows = Rows(100, columns: 10, cellLength: 100);
            var (bytes, _) = SessionDataStore.Measure(rows, new SessionUploadOptions());
            var (store, cache, budget) = Store(new SessionUploadOptions { MaxSessionBytes = bytes - 1 });

            var result = store.Upload(new SessionUploadDto { Rows = rows });

            Assert.Equal(ErrorCodes.PayloadTooLarge, result.Error);
            Assert.Equal(413, (int)result.Error!.Value / 100 % 1000);
            Assert.Equal(0, cache.Count);
            Assert.Equal(0, budget.UsedBytes);
        }

        [Fact]
        public void Measure_CountsCharactersTwiceAndAddsPerRowAndPerCellOverhead()
        {
            var rows = new List<Dictionary<string, string>>
            {
                new() { ["Name"] = "Alice", ["Qty"] = "3" },   // 4+5 and 3+1 characters
            };

            var (bytes, error) = SessionDataStore.Measure(rows, new SessionUploadOptions());

            Assert.Null(error);
            Assert.Equal(SessionDataStore.PerRowOverheadBytes + 2 * SessionDataStore.PerCellOverheadBytes + 2 * (9 + 4), bytes);
        }

        [Fact]
        public void DefaultLimits_FitFiftyThousandRowsOfTenShortColumns()
        {
            // The documented envelope: a 50k-row upload of ordinary data has to stay possible.
            var row = Enumerable.Range(0, 10).ToDictionary(c => $"Column{c:D2}", _ => "value-12345");   // 8 + 11 chars
            var (bytes, error) = SessionDataStore.Measure(Enumerable.Repeat(row, 50_000).ToList(), new SessionUploadOptions());

            Assert.Null(error);
            Assert.True(bytes <= new SessionUploadOptions().MaxSessionBytes, $"{bytes:N0} bytes");
            // ...and its JSON body fits the request limit.
            var json = JsonSerializer.SerializeToUtf8Bytes(new { rows = Enumerable.Repeat(row, 50_000) });
            Assert.True(json.Length <= SessionUploadOptions.MaxRequestBodyBytes, $"{json.Length:N0} bytes of JSON");
        }

        // ── The process-wide budget ──────────────────────────────────────────

        [Fact]
        public void Upload_OverTheGlobalBudget_IsRefusedWithoutStoring_AndFitsAgainOnceASessionIsEvicted()
        {
            var rows = Rows(50);
            var (perUpload, _) = SessionDataStore.Measure(rows, new SessionUploadOptions());
            var (store, cache, budget) = Store(new SessionUploadOptions { MaxTotalBytes = perUpload * 2 });

            var first = store.Upload(new SessionUploadDto { Rows = rows });
            var second = store.Upload(new SessionUploadDto { Rows = rows });
            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.Equal(perUpload * 2, budget.UsedBytes);

            var refused = store.Upload(new SessionUploadDto { Rows = rows });
            Assert.Equal(ErrorCodes.TooManyRequests, refused.Error);
            Assert.Equal(SessionUploadOptions.BusyMessage, refused.ErrorMessage);
            Assert.Null(refused.Data);            // no session id handed out
            Assert.Equal(2, cache.Count);         // nothing stored
            Assert.Equal(perUpload * 2, budget.UsedBytes);

            // Expiry and removal both go through the entry's post-eviction callback.
            cache.Remove(SessionDataStore.SessionCachePrefix + SessionId(first));
            Assert.Equal(perUpload, budget.UsedBytes);
            Assert.False(store.GetSchema(SessionId(first)).IsSuccess);

            var third = store.Upload(new SessionUploadDto { Rows = rows });
            Assert.True(third.IsSuccess);
            Assert.True(store.GetSchema(SessionId(third)).IsSuccess);
        }

        [Fact]
        public void Budget_ConcurrentReservations_NeverOvershoot()
        {
            var budget = new SessionUploadBudget(Options.Create(new SessionUploadOptions { MaxTotalBytes = 500 }));
            var granted = 0;

            Parallel.For(0, 2_000, _ =>
            {
                if (budget.TryReserve(1)) Interlocked.Increment(ref granted);
            });

            Assert.Equal(500, granted);
            Assert.Equal(500, budget.UsedBytes);
            Assert.Equal(0, budget.RemainingBytes);
        }

        // ── Before the body is read ──────────────────────────────────────────

        private static Mock<IMyResponseFactory> ResponseFactory()
        {
            var factory = new Mock<IMyResponseFactory>();
            factory.Setup(f => f.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                   .Returns((ErrorCodes code, string message) => new ObjectResult(message) { StatusCode = (int)code / 100 % 1000 });
            return factory;
        }

        private static async Task<(IActionResult? Result, bool Continued)> Admit(
            long? contentLength, double? memoryLoad = 0.5, SessionUploadBudget? budget = null)
        {
            var options = Options.Create(new SessionUploadOptions { MaxTotalBytes = 256L << 20 });
            var filter = new SessionUploadAdmissionFilter(
                budget ?? new SessionUploadBudget(options), options, NullLogger<SessionUploadAdmissionFilter>.Instance)
            {
                ReadMemoryLoad = () => memoryLoad,
            };

            var http = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton(ResponseFactory().Object).BuildServiceProvider(),
            };
            http.Request.ContentLength = contentLength;
            var context = new ResourceExecutingContext(
                new ActionContext(http, new RouteData(), new ActionDescriptor()), [], []);

            var continued = false;
            await filter.OnResourceExecutionAsync(context, () =>
            {
                continued = true;
                return Task.FromResult<ResourceExecutedContext>(null!);
            });
            return (context.Result, continued);
        }

        private static int Status(IActionResult? result) => Assert.IsType<ObjectResult>(result).StatusCode!.Value;

        [Fact]
        public async Task Admission_BodyOverTheRequestLimit_Is413BeforeBinding()
        {
            var (result, continued) = await Admit(SessionUploadOptions.MaxRequestBodyBytes + 1);

            Assert.False(continued);
            Assert.Equal(413, Status(result));
        }

        [Fact]
        public async Task Admission_BodyBiggerThanWhatIsLeftOfTheBudget_Is429BeforeBinding()
        {
            var budget = new SessionUploadBudget(Options.Create(new SessionUploadOptions { MaxTotalBytes = 1_000_000 }));
            Assert.True(budget.TryReserve(900_000));

            var (result, continued) = await Admit(200_000, budget: budget);

            Assert.False(continued);
            Assert.Equal(429, Status(result));
            Assert.Equal(SessionUploadOptions.BusyMessage, ((ObjectResult)result!).Value);
        }

        [Fact]
        public async Task Admission_MachineShortOfMemory_Is429BeforeBinding()
        {
            var (result, continued) = await Admit(1_000, memoryLoad: 0.99);

            Assert.False(continued);
            Assert.Equal(429, Status(result));
        }

        [Theory]
        [InlineData(1_000L, 0.5)]
        [InlineData(null, 0.5)]        // chunked: no Content-Length — [RequestSizeLimit] caps it while it's read
        [InlineData(1_000L, null)]     // no GC has run yet: nothing to judge memory by
        public async Task Admission_OrdinaryUpload_GoesThrough(long? contentLength, double? memoryLoad)
        {
            var (result, continued) = await Admit(contentLength, memoryLoad);

            Assert.True(continued);
            Assert.Null(result);
        }

        [Fact]
        public void Registration_StoreAndFilterShareOneProcessWideBudget()
        {
            var services = new ServiceCollection().AddLogging().AddMemoryCache();
            // An app setting (DataAnalysis__SessionUpload__MaxTotalBytes) overrides a default.
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DataAnalysis:SessionUpload:MaxTotalBytes"] = "1000000" })
                .Build());
            services.AddSingleton(Mock.Of<JackyAIApp.Server.Services.ITokenEncryptionService>());   // registered elsewhere in the app
            services.AddSingleton(Mock.Of<JackyAIApp.Server.Services.OpenAI.IOpenAIPromptService>());   // registered elsewhere in the app (NlQueryPlanner needs it)
            services.AddDbContext<JackyAIApp.Server.Data.AzureSQLDBContext>(o => o.UseInMemoryDatabase("di-validate"));   // registered elsewhere in the app (DataSourceResolver needs it)
            services.AddSingleton(Mock.Of<JackyAIApp.Server.Services.ICurrentUser>());   // registered elsewhere in the app (DashboardService needs it)
            services.AddDataAnalysisServices("Server=unused;User ID=u;Password=p;");
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            var store = (SessionDataStore)provider.GetRequiredService<ISessionDataStore>();
            Assert.Same(store, provider.GetRequiredService<ISessionDataStore>());
            Assert.NotNull(provider.GetRequiredService<SessionUploadAdmissionFilter>());

            var budget = provider.GetRequiredService<SessionUploadBudget>();
            Assert.Equal(1_000_000, budget.RemainingBytes);
            Assert.True(store.Upload(new SessionUploadDto { Rows = Rows(3) }).IsSuccess);
            Assert.True(budget.UsedBytes > 0);   // the store reserved against the shared instance
        }

        [Fact]
        public async Task ExceptionMiddleware_BodyOverTheSizeLimit_Stays413()
        {
            var middleware = new ExceptionMiddleware(
                _ => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
                NullLogger<ExceptionMiddleware>.Instance);
            var http = new DefaultHttpContext();

            await middleware.InvokeAsync(http);

            Assert.Equal(413, http.Response.StatusCode);
        }

        // ── Rate limit ───────────────────────────────────────────────────────

        private static HttpContext Caller(string? userId, string ip)
        {
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            http.Request.Headers["CF-Connecting-IP"] = ip;
            if (userId is not null)
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Cookies"));
            return http;
        }

        [Fact]
        public void RateLimit_EveryAnonymousCallerSharesOnePartition()
        {
            var a = RateLimitPolicies.SessionUploadPartition(Caller(null, "203.0.113.7"));
            var b = RateLimitPolicies.SessionUploadPartition(Caller(null, "198.51.100.23"));

            Assert.Equal(RateLimitPolicies.AnonymousPartition, a.PartitionKey);
            Assert.Equal(a.PartitionKey, b.PartitionKey);
        }

        [Fact]
        public void RateLimit_SignedInCallersEachGetTheirOwn()
        {
            var alice = RateLimitPolicies.SessionUploadPartition(Caller("alice", "203.0.113.7"));
            var bob = RateLimitPolicies.SessionUploadPartition(Caller("bob", "203.0.113.7"));

            Assert.NotEqual(alice.PartitionKey, bob.PartitionKey);
            Assert.NotEqual(RateLimitPolicies.AnonymousPartition, alice.PartitionKey);
        }

        [Theory]
        [InlineData(null, RateLimitPolicies.SessionUploadPermitsAnonymous)]
        [InlineData("alice", RateLimitPolicies.SessionUploadPermitsPerUser)]
        public void RateLimit_WindowAllowsItsPermitsThenRefuses(string? userId, int permits)
        {
            var partition = RateLimitPolicies.SessionUploadPartition(Caller(userId, "203.0.113.7"));
            using var limiter = partition.Factory(partition.PartitionKey);

            for (var i = 0; i < permits; i++)
                Assert.True(limiter.AttemptAcquire().IsAcquired, $"permit {i + 1}");
            Assert.False(limiter.AttemptAcquire().IsAcquired);
        }

        [Fact]
        public async Task RateLimit_Rejection_UsesTheAppErrorEnvelope()
        {
            var services = new ServiceCollection().AddLogging();
            services.AddMvcCore();
            services.AddSingleton<IMyResponseFactory>(new ResponseFactory(new ApiResponseFactory()));
            var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
            http.Response.Body = new MemoryStream();
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                new EndpointMetadataCollection(new EnableRateLimitingAttribute(RateLimitPolicies.SessionUpload)), "upload"));

            using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1, Window = TimeSpan.FromMinutes(10), QueueLimit = 0,
            });
            limiter.AttemptAcquire();
            using var lease = limiter.AttemptAcquire();

            await RateLimitPolicies.WriteRejectionAsync(new OnRejectedContext { HttpContext = http, Lease = lease }, default);

            Assert.Equal(429, http.Response.StatusCode);
            http.Response.Body.Position = 0;
            using var body = await JsonDocument.ParseAsync(http.Response.Body);
            Assert.Equal(SessionUploadOptions.BusyMessage, body.RootElement.GetProperty("error").GetProperty("message").GetString());
        }
    }
}
