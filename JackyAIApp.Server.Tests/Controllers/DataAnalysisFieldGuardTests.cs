using DataAnalysisLibrary.Core.Abstractions;
using DataAnalysisLibrary.Core.Base;
using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Enums;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Prompt;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// The schema whitelist in front of every engine-backed DataAnalysis query. The endpoints are
    /// anonymous and the default source is the app's own database, so a field name that isn't a column
    /// of the table, or an alias that isn't plain text, must be refused with 400 before the engine runs.
    /// </summary>
    public class DataAnalysisFieldGuardTests
    {
        // ── Fixture ──────────────────────────────────────────────────────────

        /// <summary>Each builder returns its request type's name, so the executor can answer per query.</summary>
        private sealed class NamedBuilder<T> : IQueryBuilder<T> where T : QueryRequestBase
        {
            public List<T> Requests { get; } = [];
            public string Build(T request) { Requests.Add(request); return typeof(T).Name; }
        }

        private sealed class Builders
        {
            public NamedBuilder<SummarizeRequest> Summarize { get; } = new();
            public NamedBuilder<DetailsRequest> Details { get; } = new();
            public NamedBuilder<FieldListRequest> FieldList { get; } = new();
            public NamedBuilder<MaxDateRequest> MaxDate { get; } = new();
            public NamedBuilder<DistinctValuesRequest> Distinct { get; } = new();
            public NamedBuilder<LookupValuesRequest> Lookup { get; } = new();
        }

        private sealed class FakeFactory(IQueryExecutor executor, Builders b) : DataSourceFactoryBase
        {
            public override string SourceKey => "mssql";
            public override IQueryBuilder<SummarizeRequest> CreateSummarizeBuilder() => b.Summarize;
            public override IQueryBuilder<DetailsRequest> CreateDetailsBuilder() => b.Details;
            public override IQueryBuilder<FieldListRequest> CreateFieldListBuilder() => b.FieldList;
            public override IQueryBuilder<MaxDateRequest> CreateMaxDateBuilder() => b.MaxDate;
            public override IQueryBuilder<DistinctValuesRequest> CreateDistinctValuesBuilder() => b.Distinct;
            public override IQueryBuilder<LookupValuesRequest> CreateLookupValuesBuilder() => b.Lookup;
            protected override IQueryExecutor CreateExecutorCore() => executor;
        }

        private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> Schema =
        [
            new Dictionary<string, object?> { ["Name"] = "Region", ["DataType"] = "nvarchar" },
            new Dictionary<string, object?> { ["Name"] = "Revenue", ["DataType"] = "decimal" },
            new Dictionary<string, object?> { ["Name"] = "OrderDate", ["DataType"] = "datetime2" },
        ];

        private const string Injection = "Region]; WAITFOR DELAY '00:00:05' --";

        private readonly Builders _builders = new();
        private readonly Mock<IQueryExecutor> _executor = new();
        private readonly DataAnalysisEngine _engine;

        public DataAnalysisFieldGuardTests()
        {
            _executor.Setup(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((string query, CancellationToken _) => query == nameof(FieldListRequest)
                         ? Schema
                         : (IReadOnlyList<IReadOnlyDictionary<string, object?>>)[]);
            _engine = new DataAnalysisEngineBuilder().AddSource(new FakeFactory(_executor.Object, _builders)).Build();
        }

        private void VerifyRan(string query, Times times) =>
            _executor.Verify(e => e.ExecuteAsync(query, It.IsAny<CancellationToken>()), times);

        private static Mock<IMyResponseFactory> ResponseFactory()
        {
            var factory = new Mock<IMyResponseFactory>();
            factory.Setup(f => f.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                   .Returns((ErrorCodes code, string message) => new ObjectResult(message) { StatusCode = (int)code / 100 % 1000 });
            factory.Setup(f => f.CreateOKResponse(It.IsAny<object>()))
                   .Returns((object data) => new OkObjectResult(data));
            return factory;
        }

        private DataAnalysisController Controller(
            IOpenAIPromptService? openAI = null, IMemoryCache? cache = null)
        {
            var prompts = new Mock<IPromptLoader>();
            prompts.Setup(p => p.GetPrompt(It.IsAny<string>())).Returns("SYSTEM");
            var controller = new DataAnalysisController(
                NullLogger<DataAnalysisController>.Instance, ResponseFactory().Object,
                new DataSourceResolver(_engine, null!, null!, NullLogger<DataSourceResolver>.Instance),
                dbContext: null!, configService: null!, sessionStore: null!,
                openAIPromptService: openAI ?? null!, usageLimitService: null!, promptLoader: prompts.Object,
                nlPlanner: new NlQueryPlanner(NullLogger<NlQueryPlanner>.Instance, openAI ?? null!),
                cache: cache ?? new MemoryCache(new MemoryCacheOptions()));
            var http = new DefaultHttpContext();
            http.Response.Body = new MemoryStream();
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            return controller;
        }

        private static (int? Status, string? Message) Error(IActionResult result)
        {
            var obj = Assert.IsAssignableFrom<ObjectResult>(result);
            return (obj.StatusCode, obj.Value as string);
        }

        private static SummarizeRequestDto Summarize(
            string viewBy = "Region", string measure = "Revenue",
            string? viewByAlias = null, string? measureAlias = null,
            string? filterField = null) => new()
        {
            ViewBy   = [new GroupFieldDto { FieldName = viewBy, Alias = viewByAlias }],
            Measures = [new MeasureFieldDto { FieldName = measure, Aggregation = AggregationType.Sum, Alias = measureAlias }],
            Filters  = filterField is null ? null : [new FilterDimensionDto { FieldName = filterField, Values = ["EMEA"] }],
        };

        // ── Aliases ──────────────────────────────────────────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Total Revenue")]
        [InlineData("銷售額")]
        [InlineData("_rows")]
        [InlineData("Avg Order Value (USD)")]
        [InlineData("Year-over-Year %")]
        [InlineData("Q1/Q2 2026.v2")]
        public void IsSafeAlias_AcceptsPlainLabels(string? alias) =>
            Assert.True(DataAnalysisFieldGuard.IsSafeAlias(alias));

        [Theory]
        [InlineData("x]; DROP TABLE Users")]
        [InlineData("a\"b")]
        [InlineData("a'b")]
        [InlineData("a`b")]
        [InlineData("a\\b")]
        [InlineData("a;b")]
        [InlineData("total -- comment")]
        [InlineData("line\nbreak")]
        public void IsSafeAlias_RejectsQuoteAndStatementCharacters(string alias) =>
            Assert.False(DataAnalysisFieldGuard.IsSafeAlias(alias));

        [Fact]
        public void IsSafeAlias_RejectsNamesLongerThanAnIdentifier() =>
            Assert.False(DataAnalysisFieldGuard.IsSafeAlias(new string('a', 129)));

        // ── Summarize ────────────────────────────────────────────────────────

        [Fact]
        public async Task Summarize_KnownFields_RunsTheQuery()
        {
            var result = await Controller().Summarize(Summarize(measureAlias: "Total Revenue", filterField: "Region"));

            Assert.IsType<OkObjectResult>(result);
            VerifyRan(nameof(SummarizeRequest), Times.Once());
            Assert.Equal("dbo.Orders", Assert.Single(_builders.Summarize.Requests).TableName);
        }

        [Fact]
        public async Task Summarize_FieldNamesAreMatchedIgnoringCase()
        {
            var result = await Controller().Summarize(Summarize(viewBy: "region", measure: "REVENUE"));

            Assert.IsType<OkObjectResult>(result);
            VerifyRan(nameof(SummarizeRequest), Times.Once());
        }

        [Theory]
        [InlineData(Injection)]
        [InlineData("Region;")]
        [InlineData("Region--")]
        [InlineData("Ghost")]
        [InlineData("")]
        public async Task Summarize_ViewByOutsideTheSchema_Is400AndNeverQueries(string field)
        {
            var result = await Controller().Summarize(Summarize(viewBy: field));

            Assert.Equal(400, Error(result).Status);
            VerifyRan(nameof(SummarizeRequest), Times.Never());
        }

        [Fact]
        public async Task Summarize_MeasureOutsideTheSchema_Is400AndNamesTheField()
        {
            var (status, message) = Error(await Controller().Summarize(Summarize(measure: "Salary")));

            Assert.Equal(400, status);
            Assert.Contains("'Salary'", message);
            VerifyRan(nameof(SummarizeRequest), Times.Never());
        }

        [Fact]
        public async Task Summarize_FilterOutsideTheSchema_Is400()
        {
            var result = await Controller().Summarize(Summarize(filterField: Injection));

            Assert.Equal(400, Error(result).Status);
            VerifyRan(nameof(SummarizeRequest), Times.Never());
        }

        [Theory]
        [InlineData("x]; DROP TABLE Users --", null)]
        [InlineData(null, "Total]] --")]
        public async Task Summarize_UnsafeAlias_Is400(string? viewByAlias, string? measureAlias)
        {
            var result = await Controller().Summarize(Summarize(viewByAlias: viewByAlias, measureAlias: measureAlias));

            Assert.Equal(400, Error(result).Status);
            VerifyRan(nameof(SummarizeRequest), Times.Never());
        }

        [Fact]
        public async Task Summarize_DefaultSourceSchemaIsReadOnceAndReused()
        {
            var controller = Controller();

            await controller.Summarize(Summarize());
            await controller.Summarize(Summarize(viewBy: "OrderDate"));

            VerifyRan(nameof(FieldListRequest), Times.Once());
            VerifyRan(nameof(SummarizeRequest), Times.Exactly(2));
        }

        [Fact]
        public async Task Summarize_SchemaUnreadable_RefusesToQuery()
        {
            _executor.Setup(e => e.ExecuteAsync(nameof(FieldListRequest), It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new InvalidOperationException("db down"));

            var result = await Controller().Summarize(Summarize());

            Assert.Equal(500, Error(result).Status);
            VerifyRan(nameof(SummarizeRequest), Times.Never());
        }

        // ── Details / export ─────────────────────────────────────────────────

        [Fact]
        public async Task Details_KnownFields_RunsTheQuery()
        {
            var result = await Controller().Details(new DetailsRequestDto
            {
                SelectFields = ["Region", "Revenue"],
                OrderBy = [new SortFieldDto { FieldName = "OrderDate", Descending = true }],
            });

            Assert.IsType<OkObjectResult>(result);
            VerifyRan(nameof(DetailsRequest), Times.Once());
        }

        [Fact]
        public async Task Details_OrderByOutsideTheSchema_Is400()
        {
            var result = await Controller().Details(new DetailsRequestDto
            {
                OrderBy = [new SortFieldDto { FieldName = Injection }],
            });

            Assert.Equal(400, Error(result).Status);
            VerifyRan(nameof(DetailsRequest), Times.Never());
        }

        [Fact]
        public async Task Details_SelectFieldOutsideTheSchema_Is400()
        {
            var result = await Controller().Details(new DetailsRequestDto { SelectFields = ["Region", "PasswordHash"] });

            Assert.Equal(400, Error(result).Status);
            VerifyRan(nameof(DetailsRequest), Times.Never());
        }

        [Fact]
        public async Task ExportDetails_FieldOutsideTheSchema_Is400WithoutAFile()
        {
            var controller = Controller();

            var result = await controller.ExportDetails(new DetailsRequestDto
            {
                SelectFields = [Injection],
                Filters = [new FilterDimensionDto { FieldName = "Region", Values = ["EMEA"] }],
            });

            Assert.Equal(400, Error(result).Status);
            Assert.Null(controller.Response.ContentType);
            VerifyRan(nameof(DetailsRequest), Times.Never());
        }

        // ── Single-field endpoints ───────────────────────────────────────────

        [Fact]
        public async Task DistinctValues_FieldOutsideTheSchema_Is400()
        {
            var result = await Controller().GetDistinctValues(Injection);

            Assert.Equal(400, Error(result).Status);
            VerifyRan(nameof(DistinctValuesRequest), Times.Never());
        }

        [Fact]
        public async Task DistinctValues_KnownField_RunsTheQuery()
        {
            await Controller().GetDistinctValues("Region");

            VerifyRan(nameof(DistinctValuesRequest), Times.Once());
        }

        [Fact]
        public async Task MaxDate_FieldOutsideTheSchema_Is400()
        {
            var result = await Controller().GetMaxDate(dateField: Injection);

            Assert.Equal(400, Error(result).Status);
            VerifyRan(nameof(MaxDateRequest), Times.Never());
        }

        [Fact]
        public async Task MaxDate_KnownField_RunsTheQuery()
        {
            await Controller().GetMaxDate(dateField: "OrderDate");

            VerifyRan(nameof(MaxDateRequest), Times.Once());
        }

        // ── Natural-language plans ───────────────────────────────────────────

        private static IOpenAIPromptService Llm(NlPlanDto plan)
        {
            var openAI = new Mock<IOpenAIPromptService>();
            openAI.Setup(o => o.GetCompletionWithSystemPromptAsync<NlPlanDto>(
                      It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                      It.IsAny<IEnumerable<(string, string)>?>(), It.IsAny<string?>()))
                  .ReturnsAsync((plan, (string?)null));
            return openAI.Object;
        }

        [Fact]
        public async Task NlSummarize_PlanWithAFieldOutsideTheSchema_IsBlocked()
        {
            var controller = Controller(Llm(new NlPlanDto
            {
                ViewBy   = [new GroupFieldDto { FieldName = Injection }],
                Measures = [new MeasureFieldDto { FieldName = "Revenue", Aggregation = AggregationType.Sum }],
            }));

            var result = await controller.NlSummarize(new NlSummarizeRequestDto { Query = "revenue by region" });

            Assert.Equal(400, Error(result).Status);
            VerifyRan(nameof(SummarizeRequest), Times.Never());
        }

        [Fact]
        public async Task NlSummarize_UnsafeAliasIsDroppedAndThePlanStillRuns()
        {
            var controller = Controller(Llm(new NlPlanDto
            {
                ViewBy   = [new GroupFieldDto { FieldName = "Region", Alias = "x]; DROP TABLE Users --" }],
                Measures = [new MeasureFieldDto { FieldName = "Revenue", Aggregation = AggregationType.Sum, Alias = "銷售額" }],
            }));

            var result = await controller.NlSummarize(new NlSummarizeRequestDto { Query = "各地區銷售額" });

            var response = Assert.IsType<NlSummarizeResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Null(Assert.Single(response.Plan.ViewBy).Alias);
            var request = Assert.Single(_builders.Summarize.Requests);
            Assert.Null(Assert.Single(request.ViewBy).Alias);
            Assert.Equal("銷售額", Assert.Single(request.Measures).Alias);
        }

        // ── Dashboard widgets ────────────────────────────────────────────────

        private async Task<IActionResult> WidgetData(string planJson)
        {
            var db = new AzureSQLDBContext(new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var dash = new Dashboard { Id = Guid.NewGuid(), UserId = "owner", Name = "Public board", Visibility = "Anyone" };
            var widget = new DashboardWidget { Id = Guid.NewGuid(), DashboardId = dash.Id, VizType = "bar", PlanJson = planJson };
            db.Dashboards.Add(dash);
            db.DashboardWidgets.Add(widget);
            await db.SaveChangesAsync();

            var controller = new DashboardController(
                NullLogger<DashboardController>.Instance, ResponseFactory().Object, db,
                new DataSourceResolver(_engine, db, null!, NullLogger<DataSourceResolver>.Instance),
                configService: null!, cache: new MemoryCache(new MemoryCacheOptions()))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            return await controller.WidgetData(dash.Id, widget.Id);
        }

        [Fact]
        public async Task WidgetData_SavedPlanWithAFieldOutsideTheSchema_Is400ForAnonymousViewers()
        {
            var result = await WidgetData("""
                {"viewBy":[{"fieldName":"Region]; WAITFOR DELAY '00:00:05' --"}],
                 "measures":[{"fieldName":"Revenue","aggregation":"Sum"}]}
                """);

            Assert.Equal(400, Error(result).Status);
            VerifyRan(nameof(SummarizeRequest), Times.Never());
        }

        [Fact]
        public async Task WidgetData_SavedPlanWithKnownFields_Runs()
        {
            var result = await WidgetData("""
                {"viewBy":[{"fieldName":"Region","alias":"地區"}],
                 "measures":[{"fieldName":"Revenue","aggregation":"Sum","alias":"Total Revenue"}]}
                """);

            Assert.IsType<OkObjectResult>(result);
            VerifyRan(nameof(SummarizeRequest), Times.Once());
        }
    }
}
