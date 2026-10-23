using System.Text.Json;
using DataAnalysisLibrary.Core.Abstractions;
using DataAnalysisLibrary.Core.Base;
using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JackyAIApp.Server.Tests.Services.DataAnalysis
{
    /// <summary>
    /// A widget's rows (<see cref="DashboardService.GetWidgetDataAsync"/>): its saved plan replayed for
    /// whoever may view the dashboard, on the source as its owner sees it — and a failure's driver text
    /// only ever reaching that owner, on their own custom source.
    /// </summary>
    public class DashboardWidgetDataTests : IDisposable
    {
        private const string Owner = "owner";
        private const string DriverText = "Login failed for user 'sa'. Server tcp:customer-db.example.com, object dbo.Sales";

        private sealed class RecordingBuilder<T> : IQueryBuilder<T> where T : QueryRequestBase
        {
            public List<T> Requests { get; } = [];
            public string Build(T request) { Requests.Add(request); return typeof(T).Name; }
        }

        private sealed class Executor(Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> answer) : IQueryExecutor
        {
            public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecuteAsync(string query, CancellationToken ct = default) =>
                Task.FromResult(answer(query));
        }

        private sealed class Factory(IQueryExecutor executor, RecordingBuilder<SummarizeRequest> summarize) : DataSourceFactoryBase
        {
            public override string SourceKey => "mssql";
            public override IQueryBuilder<SummarizeRequest> CreateSummarizeBuilder() => summarize;
            public override IQueryBuilder<DetailsRequest> CreateDetailsBuilder() => new RecordingBuilder<DetailsRequest>();
            public override IQueryBuilder<FieldListRequest> CreateFieldListBuilder() => new RecordingBuilder<FieldListRequest>();
            public override IQueryBuilder<MaxDateRequest> CreateMaxDateBuilder() => new RecordingBuilder<MaxDateRequest>();
            protected override IQueryExecutor CreateExecutorCore() => executor;
        }

        private static Dictionary<string, object?> Row(params (string Column, object? Value)[] cells) =>
            cells.ToDictionary(c => c.Column, c => c.Value);

        private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> Schema =
            [Row(("Name", "Region"), ("DataType", "nvarchar")), Row(("Name", "Revenue"), ("DataType", "decimal"))];

        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly RecordingBuilder<SummarizeRequest> _summarized = new();
        private readonly Dictionary<string, Func<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> _answers = new()
        {
            [nameof(FieldListRequest)] = () => Schema,
            [nameof(SummarizeRequest)] = () => [Row(("Region", "EMEA"), ("Revenue", 10.5m))],
        };
        private readonly Mock<IDataSourceConfigService> _configs = new();

        public DashboardWidgetDataTests()
        {
            _configs.Setup(c => c.BuildEngine(It.IsAny<DataSourceConfig>())).Returns(Engine);
            _configs.Setup(c => c.SourceKey(It.IsAny<DataSourceConnectorType>())).Returns("mssql");
        }

        public void Dispose() => _db.Dispose();

        private DataAnalysisEngine Engine() =>
            new DataAnalysisEngineBuilder()
                .AddSource(new Factory(new Executor(q => _answers.TryGetValue(q, out var a) ? a() : []), _summarized))
                .Build();

        private void Fail(string query) => _answers[query] = () => throw new InvalidOperationException(DriverText);

        private static ICurrentUser Viewer(string? userId)
        {
            var viewer = new Mock<ICurrentUser>();
            viewer.Setup(u => u.Id).Returns(userId);
            return viewer.Object;
        }

        private Task<ServiceResult<DTO.DataAnalysis.WidgetDataDto>> WidgetDataAsync(Guid dashboardId, Guid widgetId, string? viewer) =>
            new DashboardServiceBuilder { Db = _db, Viewer = Viewer(viewer), DefaultEngine = Engine(), ConfigService = _configs.Object }
                .Build().GetWidgetDataAsync(dashboardId, widgetId, default);

        private async Task<Guid> OwnersSourceAsync()
        {
            var id = Guid.NewGuid();
            _db.DataSourceConfigs.Add(new DataSourceConfig
            {
                Id = id, UserId = Owner, Name = "Mine", ConnectorType = DataSourceConnectorType.MsSql,
                ConfigJson = "encrypted", CreatedAt = DateTimeOffset.UtcNow,
            });
            await _db.SaveChangesAsync();
            return id;
        }

        private async Task<(Guid Dashboard, Guid Widget)> WidgetAsync(
            DashboardVisibility visibility = DashboardVisibility.Anyone, Guid? sourceId = null, string? planJson = null)
        {
            var context = sourceId is null ? "" : $$""","context":{"sourceId":"{{sourceId}}","tableName":"dbo.Sales"}""";
            var dashboard = new Dashboard { Id = Guid.NewGuid(), UserId = Owner, Name = "Board", Visibility = visibility };
            var widget = new DashboardWidget
            {
                Id = Guid.NewGuid(), DashboardId = dashboard.Id, VizType = "bar",
                PlanJson = planJson ?? $$"""{"viewBy":[{"fieldName":"Region","alias":"地區"}],"measures":[{"fieldName":"Revenue","aggregation":"Sum","alias":" "}]{{context}}}""",
            };
            _db.Dashboards.Add(dashboard);
            _db.DashboardWidgets.Add(widget);
            await _db.SaveChangesAsync();
            return (dashboard.Id, widget.Id);
        }

        private static (int Status, string Message) Error<T>(ServiceResult<T> result) =>
            ((int)result.Error!.Value / 100 % 1000, result.ErrorMessage!);

        // ── rows ────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task AWidget_IsItsSavedPlanRunAgain()
        {
            var (dashboard, widget) = await WidgetAsync();

            var result = await WidgetDataAsync(dashboard, widget, viewer: null);

            Assert.Equal("""[{"Region":"EMEA","Revenue":10.5}]""", JsonSerializer.Serialize(result.Data!.Rows, JsonSerializerOptions.Web));
            var request = Assert.Single(_summarized.Requests);
            Assert.Equal(("mssql", "dbo.Orders"), (request.SourceKey, request.TableName));
            Assert.Null(Assert.Single(request.Measures).Alias);   // a blank alias is no alias
        }

        [Fact]
        public async Task AWidgetsSavedFilters_GoToTheEngine()
        {
            var (dashboard, widget) = await WidgetAsync(planJson: """
                {"viewBy":[{"fieldName":"Region"}],"measures":[{"fieldName":"Revenue","aggregation":"Sum"}],
                 "filters":[{"fieldName":"Region","values":["EMEA","APAC"],"op":"In"}]}
                """);

            await WidgetDataAsync(dashboard, widget, viewer: null);

            var filter = Assert.Single(Assert.Single(_summarized.Requests).Filters);
            Assert.Equal("Region", filter.FieldName);
            Assert.Equal(["EMEA", "APAC"], filter.Values);
        }

        [Fact]
        public async Task AnyoneWhoMayViewTheBoard_SeesAWidgetOnItsOwnersCustomSource()
        {
            var source = await OwnersSourceAsync();
            var (dashboard, widget) = await WidgetAsync(sourceId: source);

            var result = await WidgetDataAsync(dashboard, widget, viewer: null);   // anonymous, on an Anyone board

            Assert.True(result.IsSuccess);
            Assert.Equal("dbo.Sales", Assert.Single(_summarized.Requests).TableName);
        }

        // ── failures ────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(Owner, DriverText)]
        [InlineData("friend", "Query failed.")]
        [InlineData(null, "Query failed.")]
        public async Task AFailedQueryOnACustomSource_ShowsTheDriverTextOnlyToItsOwner(string? viewer, string message)
        {
            var source = await OwnersSourceAsync();
            var (dashboard, widget) = await WidgetAsync(viewer == "friend" ? DashboardVisibility.Public : DashboardVisibility.Anyone, source);
            Fail(nameof(SummarizeRequest));

            Assert.Equal((500, message), Error(await WidgetDataAsync(dashboard, widget, viewer)));
        }

        [Fact]
        public async Task AFailedQueryOnTheDefaultSource_ShowsEvenTheOwnerOnlyTheGenericText()
        {
            var (dashboard, widget) = await WidgetAsync();
            Fail(nameof(SummarizeRequest));

            Assert.Equal((500, "Query failed."), Error(await WidgetDataAsync(dashboard, widget, Owner)));
        }

        [Fact]
        public async Task ASourceThatNoLongerExists_IsNotFound_InTheSameWordsAsALiveQuery()
        {
            var (dashboard, widget) = await WidgetAsync(sourceId: Guid.NewGuid());

            Assert.Equal((404, "Source not found."), Error(await WidgetDataAsync(dashboard, widget, Owner)));
        }

        [Fact]
        public async Task ASourceWhoseSavedSettingsAreRefused_IsABadRequestWithTheReason()
        {
            var source = await OwnersSourceAsync();
            _configs.Setup(c => c.BuildEngine(It.IsAny<DataSourceConfig>()))
                    .Throws(new DataSourceConfigException("Integrated Security is not allowed."));
            var (dashboard, widget) = await WidgetAsync(sourceId: source);

            Assert.Equal((400, "Integrated Security is not allowed."), Error(await WidgetDataAsync(dashboard, widget, Owner)));
        }

        [Fact]
        public async Task AnUnreadableSchema_RunsNothing()
        {
            var (dashboard, widget) = await WidgetAsync();
            Fail(nameof(FieldListRequest));

            Assert.Equal((500, "Schema query failed."), Error(await WidgetDataAsync(dashboard, widget, Owner)));
            Assert.Empty(_summarized.Requests);
        }

        [Fact]
        public async Task AWidgetNotOnTheBoard_IsNotFound()
        {
            var (dashboard, _) = await WidgetAsync();

            Assert.Equal((404, "Widget not found."), Error(await WidgetDataAsync(dashboard, Guid.NewGuid(), Owner)));
        }

        [Fact]
        public async Task ABoardTheViewerMayNotSee_IsNotFound_BeforeAnythingRuns()
        {
            var (dashboard, widget) = await WidgetAsync(DashboardVisibility.Private);

            Assert.Equal((404, "Dashboard not found."), Error(await WidgetDataAsync(dashboard, widget, "friend")));
            Assert.Empty(_summarized.Requests);
        }
    }
}
