using DataAnalysisLibrary.Core.Abstractions;
using DataAnalysisLibrary.Core.Base;
using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Models.Common;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// The NL agent reads every String column's real values before each question
    /// (DataAnalysisLibrary 1.2 DistinctValues / LookupValues). These pin that a slow or failing source
    /// degrades to "unknown" instead of failing the question, that a timeout is remembered so later
    /// questions don't pay it again, and that the session source mirrors the engine semantics.
    /// </summary>
    public class NlValueSourcesTests
    {
        /// <summary>Returns canned rows (optionally after a delay that ignores cancellation, like ADX) and counts queries.</summary>
        private sealed class FakeExecutor(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, TimeSpan delay) : IQueryExecutor
        {
            public int Calls { get; private set; }

            public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecuteAsync(string queryString, CancellationToken cancellationToken = default)
            {
                Calls++;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, CancellationToken.None);
                return rows;
            }
        }

        private sealed class FixedBuilder<T> : IQueryBuilder<T> where T : QueryRequestBase
        {
            public string Build(T request) => "query";
        }

        private sealed class FakeFactory(IQueryExecutor executor) : DataSourceFactoryBase
        {
            public override string SourceKey => "fake";
            public override IQueryBuilder<DistinctValuesRequest> CreateDistinctValuesBuilder() => new FixedBuilder<DistinctValuesRequest>();
            public override IQueryBuilder<LookupValuesRequest> CreateLookupValuesBuilder() => new FixedBuilder<LookupValuesRequest>();
            protected override IQueryExecutor CreateExecutorCore() => executor;
        }

        private static IReadOnlyDictionary<string, object?> Row(string? field, string? value) =>
            new Dictionary<string, object?> { [DistinctValueColumns.Field] = field, [DistinctValueColumns.Value] = value };

        private static (EngineNlValueSource Source, FakeExecutor Executor) Engine(
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, TimeSpan delay = default, IMemoryCache? cache = null)
        {
            var executor = new FakeExecutor(rows, delay);
            var engine   = new DataAnalysisEngineBuilder().AddSource(new FakeFactory(executor)).Build();
            var source   = new EngineNlValueSource(engine, "fake", "dbo.Orders", "default",
                cache ?? new MemoryCache(new MemoryCacheOptions()), NullLogger.Instance,
                distinctTimeout: TimeSpan.FromMilliseconds(200), lookupTimeout: TimeSpan.FromMilliseconds(200));
            return (source, executor);
        }

        [Fact]
        public async Task Engine_Distinct_MapsTruncationAndDropsEmptyValues()
        {
            var (source, _) = Engine([Row("Region", "APAC"), Row("Region", null), Row("Region", ""), Row("Customer", "A"), Row("Customer", "B")]);

            var values = await source.DistinctAsync(["Region", "Customer"], limit: 1, default);

            Assert.Equal(["APAC"], values!["Region"].Values);
            Assert.True(values["Region"].IsTruncated);      // 3 rows for a limit of 1
            Assert.Equal(["A"], values["customer"].Values);
            Assert.True(values["Customer"].IsTruncated);
        }

        [Fact]
        public async Task Engine_Distinct_IsCachedPerSourceAndTable()
        {
            var (source, executor) = Engine([Row("Region", "APAC")]);

            await source.DistinctAsync(["Region"], 300, default);
            await source.DistinctAsync(["Region"], 300, default);

            Assert.Equal(1, executor.Calls);
        }

        [Fact]
        public async Task Engine_Distinct_TimeoutIsUnknownAndRemembered()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var (slow, slowExecutor) = Engine([Row("Region", "APAC")], delay: TimeSpan.FromSeconds(30), cache: cache);

            var started = DateTime.UtcNow;
            var first   = await slow.DistinctAsync(["Region"], 300, default);
            Assert.Null(first);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3)); // the 30 s query is abandoned at 200 ms

            // A later question on the same source gets "unknown" straight from the cache.
            var second = await slow.DistinctAsync(["Region"], 300, default);
            Assert.Null(second);
            Assert.Equal(1, slowExecutor.Calls);
        }

        [Fact]
        public async Task Engine_Lookup_ReturnsMatches()
        {
            var (source, _) = Engine([Row(null, "Acme Corp")]);

            Assert.Equal(["Acme Corp"], await source.LookupAsync("Customer", ["acme corp"], default));
        }

        // ── Session source ──────────────────────────────────────────────────────

        private static SessionNlValueSource Session()
        {
            var store = new SessionDataStore(new MemoryCache(new MemoryCacheOptions()));
            var upload = store.Upload(new SessionUploadDto
            {
                Rows =
                [
                    new() { ["Region"] = "APAC", ["Customer"] = "Acme Corp" },
                    new() { ["Region"] = "EMEA", ["Customer"] = "Globex" },
                    new() { ["Region"] = "APAC", ["Customer"] = "Acme Holdings" },
                ],
            });
            return new SessionNlValueSource(store, ((SessionUploadResponseDto)upload.Data!).SessionId);
        }

        [Fact]
        public async Task Session_Distinct_FlagsTruncationOverLimit()
        {
            var values = await Session().DistinctAsync(["Region", "Customer"], limit: 2, default);

            Assert.Equal(["APAC", "EMEA"], values!["Region"].Values);
            Assert.False(values["Region"].IsTruncated);
            Assert.Equal(2, values["Customer"].Values.Count);
            Assert.True(values["Customer"].IsTruncated);
        }

        [Fact]
        public async Task Session_Lookup_IsCaseInsensitive_AndReturnsStoredSpelling()
        {
            Assert.Equal(["Globex"], await Session().LookupAsync("Customer", ["GLOBEX", "Initech"], default));
        }

        [Fact]
        public async Task Session_Search_FindsContainingValues()
        {
            Assert.Equal(["Acme Corp", "Acme Holdings"], await Session().SearchAsync("Customer", "acme", 20, default));
        }
    }
}
