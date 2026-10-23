using System.Text.Json;
using DataAnalysisLibrary.Core.Abstractions;
using DataAnalysisLibrary.Core.Base;
using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Enums;
using DataAnalysisLibrary.Core.Models.Common;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Services.Prompt;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// What each DataAnalysis query endpoint answers — the payload's JSON on success, the status and
    /// message otherwise — for the engine-backed sources and the natural-language question.
    /// </summary>
    public class DataAnalysisControllerResponseTests
    {
        // ── the engine ──────────────────────────────────────────────────────────────────────────

        private sealed class RecordingBuilder<T> : IQueryBuilder<T> where T : QueryRequestBase
        {
            public List<T> Requests { get; } = [];
            public string Build(T request) { Requests.Add(request); return typeof(T).Name; }
        }

        private sealed class Builders
        {
            public RecordingBuilder<SummarizeRequest> Summarize { get; } = new();
            public RecordingBuilder<DetailsRequest> Details { get; } = new();
        }

        private sealed class Executor(Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> answer) : IQueryExecutor
        {
            public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecuteAsync(string query, CancellationToken ct = default) =>
                Task.FromResult(answer(query));
        }

        private sealed class Factory(IQueryExecutor executor, Builders builders) : DataSourceFactoryBase
        {
            public override string SourceKey => "mssql";
            public override IQueryBuilder<SummarizeRequest> CreateSummarizeBuilder() => builders.Summarize;
            public override IQueryBuilder<DetailsRequest> CreateDetailsBuilder() => builders.Details;
            public override IQueryBuilder<FieldListRequest> CreateFieldListBuilder() => new RecordingBuilder<FieldListRequest>();
            public override IQueryBuilder<MaxDateRequest> CreateMaxDateBuilder() => new RecordingBuilder<MaxDateRequest>();
            public override IQueryBuilder<DistinctValuesRequest> CreateDistinctValuesBuilder() => new RecordingBuilder<DistinctValuesRequest>();
            public override IQueryBuilder<LookupValuesRequest> CreateLookupValuesBuilder() => new RecordingBuilder<LookupValuesRequest>();
            protected override IQueryExecutor CreateExecutorCore() => executor;
        }

        private static Dictionary<string, object?> Row(params (string Column, object? Value)[] cells) =>
            cells.ToDictionary(c => c.Column, c => c.Value);

        private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> Schema =
        [
            Row(("Name", "Region"), ("DataType", "nvarchar")),
            Row(("Name", "Revenue"), ("DataType", "decimal")),
            Row(("Name", "OrderDate"), ("DataType", "datetime2")),
        ];

        private readonly Builders _builders = new();
        private readonly Dictionary<string, Func<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> _answers = new()
        {
            [nameof(FieldListRequest)] = () => Schema,
        };
        private readonly Mock<IPromptLoader> _prompts = new();
        private readonly Mock<INlQueryPlanner> _planner = new();
        private readonly List<(List<(string Name, string CanonicalType)> Fields, INlValueSource Source, Func<string, Task<string?>> MaxDate)> _planned = [];

        public DataAnalysisControllerResponseTests()
        {
            _prompts.Setup(p => p.GetPrompt("Prompt/DataAnalysis/NlSummarizeSystem.txt")).Returns("NL RULES");
        }

        private void Answer(string query, params Dictionary<string, object?>[] rows) => _answers[query] = () => rows;

        private void Fail(string query) => _answers[query] = () => throw new InvalidOperationException("driver says no");

        private DataAnalysisEngine Engine() =>
            new DataAnalysisEngineBuilder()
                .AddSource(new Factory(new Executor(q => _answers.TryGetValue(q, out var a) ? a() : []), _builders))
                .Build();

        private JackyAIApp.Server.Controllers.DataAnalysisController Controller(
            ISessionDataStore? sessions = null, AzureSQLDBContext? db = null, string? userId = null) =>
            new DataAnalysisControllerBuilder
            {
                DefaultEngine = Engine(), Sessions = sessions, Db = db, UserId = userId,
                Prompts = _prompts.Object, NlPlanner = _planner.Object,
            }.Build();

        private static string Json(IActionResult result) =>
            JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value, JsonSerializerOptions.Web);

        private static (int Status, string Message) Error(IActionResult result)
        {
            var error = Assert.IsType<ObjectResult>(result);
            return (error.StatusCode!.Value, Assert.IsType<string>(error.Value));
        }

        // ── the engine endpoints ────────────────────────────────────────────────────────────────

        [Fact]
        public async Task TheSchema_IsTheEnginesFieldList()
        {
            Assert.Equal(
                """{"fields":[{"name":"Region","dataType":"nvarchar","isNullable":false,"canonicalType":0},{"name":"Revenue","dataType":"decimal","isNullable":false,"canonicalType":0},{"name":"OrderDate","dataType":"datetime2","isNullable":false,"canonicalType":0}]}""",
                Json(await Controller().GetSchema()));
        }

        [Fact]
        public async Task AnUnreadableSchema_IsA500WithAGenericMessage()
        {
            Fail(nameof(FieldListRequest));

            Assert.Equal((500, "Schema query failed."), Error(await Controller().GetSchema()));
        }

        [Fact]
        public async Task TheMaxDate_IsTheColumnsLatestValue()
        {
            Answer(nameof(MaxDateRequest), Row(("MaxDate", new DateTime(2026, 9, 30))));

            Assert.Equal("""{"maxDate":"2026-09-30T00:00:00+00:00"}""", Json(await Controller().GetMaxDate(dateField: "OrderDate")));
        }

        [Fact]
        public async Task TheDistinctValues_LeaveOutEmptyAndNullOnes()
        {
            Answer(nameof(DistinctValuesRequest),
                Row((DistinctValueColumns.Field, "Region"), (DistinctValueColumns.Value, "EMEA")),
                Row((DistinctValueColumns.Field, "Region"), (DistinctValueColumns.Value, "")),
                Row((DistinctValueColumns.Field, "Region"), (DistinctValueColumns.Value, null)),
                Row((DistinctValueColumns.Field, "Region"), (DistinctValueColumns.Value, "APAC")));

            Assert.Equal("""{"values":["EMEA","APAC"]}""", Json(await Controller().GetDistinctValues("Region")));
        }

        [Fact]
        public async Task DistinctValuesThatCannotBeRead_AreNoValues_NotAFailure()
        {
            Fail(nameof(DistinctValuesRequest));

            Assert.Equal("""{"values":[]}""", Json(await Controller().GetDistinctValues("Region")));
        }

        [Fact]
        public async Task DistinctValues_NeedAField()
        {
            Assert.Equal((400, "field is required."), Error(await Controller().GetDistinctValues(" ")));
        }

        [Fact]
        public async Task Summarize_AnswersTheEnginesRows()
        {
            Answer(nameof(SummarizeRequest), Row(("Region", "EMEA"), ("Revenue", 10.5m)));

            var result = await Controller().Summarize(new SummarizeRequestDto
            {
                ViewBy = [new GroupFieldDto { FieldName = "Region" }],
                Measures = [new MeasureFieldDto { FieldName = "Revenue", Aggregation = AggregationType.Sum, Alias = " " }],
            });

            Assert.Equal("""{"rows":[{"Region":"EMEA","Revenue":10.5}]}""", Json(result));
            var request = Assert.Single(_builders.Summarize.Requests);
            Assert.Equal(("mssql", "dbo.Orders"), (request.SourceKey, request.TableName));
            Assert.Null(Assert.Single(request.Measures).Alias);   // a blank alias is no alias
        }

        [Fact]
        public async Task Details_AnswersOnePage_AtMost200Rows()
        {
            Answer(nameof(DetailsRequest), Row(("Region", "EMEA"), ("Revenue", 10.5m)));

            var result = await Controller().Details(new DetailsRequestDto
            {
                SelectFields = ["Region", "Revenue"], Skip = 40, Take = 500,
                OrderBy = [new SortFieldDto { FieldName = "Revenue", Descending = true }],
            });

            Assert.Equal("""{"rows":[{"Region":"EMEA","Revenue":10.5}],"totalCount":41}""", Json(result));   // the 40 skipped and the 1 sent
            var request = Assert.Single(_builders.Details.Requests);
            Assert.Equal((40, 200), (request.Skip, request.Take));
            Assert.Equal(("Revenue", true), (request.OrderBy.Single().FieldName, request.OrderBy.Single().Descending));
        }

        [Fact]
        public async Task ACustomSourceThatIsNotTheCallers_IsNotFound()
        {
            await using var db = new AzureSQLDBContext(new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var result = await Controller(db: db, userId: "someone").Summarize(new SummarizeRequestDto
            {
                ViewBy = [new GroupFieldDto { FieldName = "Region" }],
                Measures = [new MeasureFieldDto { FieldName = "Revenue", Aggregation = AggregationType.Sum }],
                Context = new QueryContextDto { SourceId = Guid.NewGuid(), TableName = "dbo.Sales" },
            });

            Assert.Equal((404, "Source not found."), Error(result));
        }

        // ── the natural-language question ───────────────────────────────────────────────────────

        private static SummarizeRequestDto RegionRevenue(string? alias = null) => new()
        {
            ViewBy = [new GroupFieldDto { FieldName = "Region", Alias = alias }],
            Measures = [new MeasureFieldDto { FieldName = "Revenue", Aggregation = AggregationType.Sum }],
        };

        private void PlannerAnswers(NlPlanResult result) =>
            _planner.Setup(p => p.BuildPlanAsync(
                    It.IsAny<List<(string, string)>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<INlValueSource>(),
                    It.IsAny<Func<string, Task<string?>>>(), It.IsAny<CancellationToken>()))
                .Callback((List<(string, string)> fields, string _, string _, INlValueSource source, Func<string, Task<string?>> maxDate, CancellationToken _) =>
                    _planned.Add((fields, source, maxDate)))
                .ReturnsAsync(result);

        [Fact]
        public async Task WithoutItsPrompt_TheQuestionIsA500()
        {
            _prompts.Setup(p => p.GetPrompt(It.IsAny<string>())).Returns((string?)null);

            Assert.Equal((500, "Prompt configuration error."),
                Error(await Controller().NlSummarize(new NlSummarizeRequestDto { Query = "revenue by region" })));
        }

        [Fact]
        public async Task OnTheEngine_ThePlanRuns_AndTheAnswerCarriesItsNotesOnlyWhenThereAreAny()
        {
            Answer(nameof(SummarizeRequest), Row(("Region", "EMEA"), ("Revenue", 10.5m)));
            Answer(nameof(MaxDateRequest), Row(("MaxDate", new DateTime(2026, 9, 30))));
            var plan = RegionRevenue();
            PlannerAnswers(new NlPlanResult(plan, null, null, [], ["越南"]));

            var result = await Controller().NlSummarize(new NlSummarizeRequestDto { Query = "revenue by region in Vietnam" });

            var answer = Assert.IsType<NlSummarizeResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(NlPlanValidator.BuildInterpretation(plan), answer.Interpretation);
            Assert.Equal("""[{"Region":"EMEA","Revenue":10.5}]""", JsonSerializer.Serialize(answer.Rows, JsonSerializerOptions.Web));
            Assert.Null(answer.Notes);
            Assert.Equal(["越南"], answer.Unmatched);
            Assert.Equal("Region", Assert.Single(answer.Plan.ViewBy).FieldName);

            var (fields, source, maxDate) = Assert.Single(_planned);
            Assert.Equal([("Region", "Unknown"), ("Revenue", "Unknown"), ("OrderDate", "Unknown")], fields);   // the fake's types map to none
            Assert.IsType<EngineNlValueSource>(source);
            Assert.Equal("2026-09-30", await maxDate("OrderDate"));
        }

        [Fact]
        public async Task OnTheEngine_ALatestDateThatCannotBeRead_IsNoDate()
        {
            Fail(nameof(MaxDateRequest));
            PlannerAnswers(new NlPlanResult(RegionRevenue(), null, null, ["已忽略"], []));

            var result = await Controller().NlSummarize(new NlSummarizeRequestDto { Query = "revenue by region" });

            var answer = Assert.IsType<NlSummarizeResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(["已忽略"], answer.Notes);
            Assert.Null(answer.Unmatched);
            Assert.Null(await Assert.Single(_planned).MaxDate("OrderDate"));
        }

        [Fact]
        public async Task OnTheEngine_AnUnreadableSchema_IsA500()
        {
            Fail(nameof(FieldListRequest));

            Assert.Equal((500, "Schema query failed."),
                Error(await Controller().NlSummarize(new NlSummarizeRequestDto { Query = "revenue by region" })));
            Assert.Empty(_planned);
        }

        [Fact]
        public async Task OnTheEngine_AQuestionThatCannotBePlanned_IsThePlannersError()
        {
            PlannerAnswers(new NlPlanResult(null, ErrorCodes.BadRequest, "AI could not interpret your query.", [], []));

            Assert.Equal((400, "AI could not interpret your query."),
                Error(await Controller().NlSummarize(new NlSummarizeRequestDto { Query = "???" })));
        }

        [Fact]
        public async Task OnTheEngine_APlanThatFailsToRun_IsA500()
        {
            Fail(nameof(SummarizeRequest));
            PlannerAnswers(new NlPlanResult(RegionRevenue(), null, null, [], []));

            Assert.Equal((500, "Query execution failed."),
                Error(await Controller().NlSummarize(new NlSummarizeRequestDto { Query = "revenue by region" })));
        }

        private static (SessionDataStore Store, string SessionId) Session(MemoryCache? cache = null)
        {
            var store = new SessionDataStore(cache ?? new MemoryCache(new MemoryCacheOptions()));
            var upload = store.Upload(new SessionUploadDto
            {
                Name = "Sales 2026",
                Rows =
                [
                    new() { ["Region"] = "EMEA", ["Amount"] = "5", ["Day"] = "2026-01-02" },
                    new() { ["Region"] = "APAC", ["Amount"] = "20", ["Day"] = "2026-01-04" },
                ],
            });
            return (store, upload.Data!.SessionId);
        }

        private static SummarizeRequestDto RegionAmount() => new()
        {
            ViewBy = [new GroupFieldDto { FieldName = "Region" }],
            Measures = [new MeasureFieldDto { FieldName = "Amount", Aggregation = AggregationType.Sum }],
        };

        [Fact]
        public async Task OnASession_ThePlanRunsOnItsRows()
        {
            var (store, sessionId) = Session();
            var plan = RegionAmount();
            PlannerAnswers(new NlPlanResult(plan, null, null, ["已忽略"], ["越南"]));

            var result = await Controller(store).NlSummarize(new NlSummarizeRequestDto { Query = "amount by region", SessionId = sessionId });

            var answer = Assert.IsType<NlSummarizeResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Same(plan, answer.Plan);
            Assert.Equal(NlPlanValidator.BuildInterpretation(plan), answer.Interpretation);
            Assert.Equal(JsonSerializer.Serialize(store.Summarize(sessionId, plan).Data!.Rows, JsonSerializerOptions.Web),
                JsonSerializer.Serialize(answer.Rows, JsonSerializerOptions.Web));
            Assert.Equal(["已忽略"], answer.Notes);
            Assert.Equal(["越南"], answer.Unmatched);

            var (fields, source, maxDate) = Assert.Single(_planned);
            Assert.Equal(store.GetSchema(sessionId).Data!.Fields.Select(f => (f.Name, f.CanonicalType)), fields);
            Assert.IsType<SessionNlValueSource>(source);
            Assert.Equal(store.GetMaxDate(sessionId, "Day").Data!.MaxDate, await maxDate("Day"));
            Assert.Null(await maxDate("NoSuchColumn"));
        }

        [Fact]
        public async Task OnASession_ThatDoesNotExist_IsTheStoresError()
        {
            var (store, _) = Session();
            var missing = store.GetSchema("nope");

            var result = await Controller(store).NlSummarize(new NlSummarizeRequestDto { Query = "amount by region", SessionId = "nope" });

            Assert.Equal(((int)missing.Error!.Value / 100 % 1000, missing.ErrorMessage!), Error(result));
            Assert.Empty(_planned);
        }

        [Fact]
        public async Task OnASession_AQuestionThatCannotBePlanned_IsThePlannersError()
        {
            var (store, sessionId) = Session();
            PlannerAnswers(new NlPlanResult(null, ErrorCodes.BadRequest, "AI referenced an unknown field.", [], []));

            Assert.Equal((400, "AI referenced an unknown field."),
                Error(await Controller(store).NlSummarize(new NlSummarizeRequestDto { Query = "?", SessionId = sessionId })));
        }

        [Fact]
        public async Task OnASession_ThatExpiresWhileThePlanIsMade_IsTheStoresError()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var (store, sessionId) = Session(cache);
            _planner.Setup(p => p.BuildPlanAsync(
                    It.IsAny<List<(string, string)>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<INlValueSource>(),
                    It.IsAny<Func<string, Task<string?>>>(), It.IsAny<CancellationToken>()))
                .Callback(() => cache.Compact(1.0))   // the session is gone by the time the plan runs
                .ReturnsAsync(new NlPlanResult(RegionAmount(), null, null, [], []));

            var result = await Controller(store).NlSummarize(new NlSummarizeRequestDto { Query = "amount by region", SessionId = sessionId });

            Assert.Equal((404, "Session not found or expired."), Error(result));
        }
    }
}
