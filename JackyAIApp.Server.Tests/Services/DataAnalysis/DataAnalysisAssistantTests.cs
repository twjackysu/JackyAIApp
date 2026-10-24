using System.Text.Json;
using DataAnalysisLibrary.Core.Enums;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Assistant;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis.Assistant;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace JackyAIApp.Server.Tests.Services.DataAnalysis
{
    /// <summary>The Data Analysis assistant's profile and its ask_data tool (the page's 問 AI pipeline, mocked).</summary>
    public class DataAnalysisAssistantTests
    {
        private static readonly DateOnly Today = new(2026, 10, 24);
        private static readonly Guid Source = Guid.Parse("11111111-2222-3333-4444-555555555555");
        private static readonly AiToolContext Ctx = new("u1", "conv-1");

        private readonly Mock<INlSummarizeService> _nl = new();
        private readonly Mock<IUsageLimitService> _usage = new();
        private readonly Mock<IDataAnalysisQueryService> _queries = new();
        private readonly Mock<ISessionDataStore> _sessions = new();
        private readonly List<IAiTool> _connectorTools = [];
        private readonly Mock<IDashboardService> _dashboards = new();
        private readonly DataAnalysisCharts _charts = new(new MemoryCache(new MemoryCacheOptions()));
        private NlSummarizeRequestDto? _asked;

        public DataAnalysisAssistantTests()
        {
            _usage.Setup(u => u.TryConsumeAsync("u1", UsageFeature.NlSummarize, It.IsAny<ulong>())).ReturnsAsync(true);
            _nl.Setup(n => n.SummarizeAsync(It.IsAny<NlSummarizeRequestDto>(), It.IsAny<CancellationToken>()))
                .Callback<NlSummarizeRequestDto, CancellationToken>((dto, _) => _asked = dto)
                .ReturnsAsync(ServiceResult.Ok(new NlSummarizeResponseDto
                {
                    Plan = new SummarizeRequestDto
                    {
                        ViewBy = [new GroupFieldDto { Type = "DateTrunc", FieldName = "OrderDate", Alias = "月份", Granularity = "Month" }],
                        Measures = [new MeasureFieldDto { FieldName = "Amount", Aggregation = AggregationType.Sum, Alias = "營收" }],
                    },
                    Interpretation = "依月份加總營收",
                    Rows = new object[]
                    {
                        new Dictionary<string, object?> { ["月份"] = "2026-01", ["營收"] = 100 },
                        new Dictionary<string, object?> { ["月份"] = "2026-02", ["營收"] = 120 },
                    },
                    Notes = ["資料裡沒有「越南」"],
                }));
        }

        private static ChatProfileContext Page(params (string Key, string Value)[] values) =>
            new("u1", Today, values.ToDictionary(v => v.Key, v => v.Value));

        /// <summary>The profile's setup, and its ask_data tool (or the tool named).</summary>
        private async Task<(ChatProfileSetup Setup, IAiTool Tool)> SetupAsync(ChatProfileContext context, string tool = AskDataTool.Name)
        {
            var connectors = new Mock<IConnectorToolSource>();
            connectors.Setup(c => c.ForUserAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(() => _connectorTools.ToList());
            var setup = await new DataAnalysisChatProfile(
                    _nl.Object, _queries.Object, _sessions.Object, _usage.Object, connectors.Object, _dashboards.Object, _charts)
                .SetupAsync(context, CancellationToken.None);
            return (setup, Assert.Single(setup.Tools!, t => t.Descriptor.Name == tool));
        }

        private static JsonElement Input(object value) => JsonSerializer.SerializeToElement(value);

        [Fact]
        public async Task TheProfile_HasItsPromptAndItsTools_OnlySavingWrites_AndNoTwStock()
        {
            var (setup, tool) = await SetupAsync(Page());

            Assert.Equal(DataAnalysisChatProfile.SystemPromptFor(Today), setup.SystemPrompt);
            Assert.Contains("2026-10-24", setup.SystemPrompt);
            Assert.False(setup.AttachTwStock);
            Assert.Null(setup.CachedBackground);
            Assert.Equal(new[] { AskDataTool.Name, DescribeDataTool.Name, SaveToDashboardTool.Name }, setup.Tools!.Select(t => t.Descriptor.Name));
            Assert.Equal(new[] { SaveToDashboardTool.Name }, setup.Tools!.Where(t => t.Descriptor.Effect != ToolEffect.Read).Select(t => t.Descriptor.Name));
            Assert.Equal("object", tool.Descriptor.InputSchema.GetProperty("type").GetString());
        }

        [Fact]
        public async Task TheReadersConnectors_AreOfferedAfterThePagesTools_AndThePromptSaysTheyAreOutside()
        {
            var outside = new Mock<IAiTool>();
            outside.Setup(t => t.Descriptor).Returns(new AiToolDescriptor("m1__search", "Notion・搜尋", "說明",
                JsonSerializer.SerializeToElement(new { type = "object" }), ToolEffect.Read, "inst-1", "Notion"));
            _connectorTools.Add(outside.Object);

            var (setup, _) = await SetupAsync(Page());

            Assert.Equal(new[] { AskDataTool.Name, DescribeDataTool.Name, SaveToDashboardTool.Name, "m1__search" }, setup.Tools!.Select(t => t.Descriptor.Name));
            Assert.Equal(DataAnalysisChatProfile.SystemPromptFor(Today, hasConnectors: true), setup.SystemPrompt);
            Assert.Contains("外部 connector", setup.SystemPrompt);
        }

        [Fact]
        public async Task AskData_AsksThePagesSource_AndAnswersWithTheRowsAndTheNotes()
        {
            var (_, tool) = await SetupAsync(Page(("sourceId", Source.ToString()), ("tableName", "dbo.Orders")));

            var result = await tool.RunAsync(Input(new { question = "每月營收", chart = "line" }), Ctx, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Equal("每月營收", _asked!.Query);
            Assert.Equal(Source, _asked.Context!.SourceId);
            Assert.Equal("dbo.Orders", _asked.Context.TableName);
            Assert.Null(_asked.SessionId);
            var content = JsonDocument.Parse(result.Content).RootElement;
            Assert.Equal(2, content.GetProperty("rowCount").GetInt32());
            Assert.Equal("資料裡沒有「越南」", content.GetProperty("notes")[0].GetString());
            Assert.Contains("越南", result.Content); // Chinese kept as it is for the model
        }

        [Fact]
        public async Task AskData_ShowsAChartCard_WithItsHints_AndAPlanForTheDashboard()
        {
            var (_, tool) = await SetupAsync(Page(("sourceId", Source.ToString()), ("tableName", "dbo.Orders")));

            var result = await tool.RunAsync(Input(new { question = "每月營收", chart = "line", title = "每月營收" }), Ctx, CancellationToken.None);

            var card = JsonSerializer.SerializeToElement(result.Card, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal("chart", card.GetProperty("kind").GetString());
            Assert.Equal("每月營收", card.GetProperty("title").GetString());
            var data = card.GetProperty("data");
            Assert.Equal("line", data.GetProperty("viz").GetString());
            Assert.Equal(new[] { "月份", "營收" }, data.GetProperty("headers").EnumerateArray().Select(h => h.GetString()));
            Assert.Equal(2, data.GetProperty("rows").GetArrayLength());
            Assert.Equal("月份", data.GetProperty("groupHints")[0].GetString());
            Assert.Equal("月份", data.GetProperty("temporalHints")[0].GetString());
            Assert.Equal(Source.ToString(), data.GetProperty("plan").GetProperty("context").GetProperty("sourceId").GetString());
        }

        [Fact]
        public async Task AskData_OnAnUploadedFile_AsksItsSession_AndItsCardHasNoPlan()
        {
            var (_, tool) = await SetupAsync(Page(("sessionId", "s-1"), ("sourceId", Source.ToString())));

            var result = await tool.RunAsync(Input(new { question = "總計" }), Ctx, CancellationToken.None);

            Assert.Equal("s-1", _asked!.SessionId);
            Assert.Null(_asked.Context);
            var data = JsonSerializer.SerializeToElement(result.Card, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("data");
            Assert.Equal(AskDataTool.DefaultChart, data.GetProperty("viz").GetString());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("plan").ValueKind);
        }

        [Fact]
        public async Task AskData_IsChargedAsAQuestion_AndOverTheLimitAsksNothing()
        {
            _usage.Setup(u => u.TryConsumeAsync("u1", UsageFeature.NlSummarize, It.IsAny<ulong>())).ReturnsAsync(false);
            var (_, tool) = await SetupAsync(Page());

            var result = await tool.RunAsync(Input(new { question = "總計" }), Ctx, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal(AskDataTool.LimitMessage, result.Content);
            _usage.Verify(u => u.TryConsumeAsync("u1", UsageFeature.NlSummarize, UsageCosts.Of(UsageFeature.NlSummarize)), Times.Once);
            _nl.Verify(n => n.SummarizeAsync(It.IsAny<NlSummarizeRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"question\":\"  \"}")]
        [InlineData("{\"question\":3}")]
        public async Task AskData_WithoutAQuestion_IsAnError_AndChargesNothing(string json)
        {
            var (_, tool) = await SetupAsync(Page());

            var result = await tool.RunAsync(JsonDocument.Parse(json).RootElement, Ctx, CancellationToken.None);

            Assert.True(result.IsError);
            _usage.Verify(u => u.TryConsumeAsync(It.IsAny<string>(), It.IsAny<UsageFeature>(), It.IsAny<ulong>()), Times.Never);
        }

        [Fact]
        public async Task AskData_ThatFails_TellsTheModelWhy_WithoutACard()
        {
            _nl.Setup(n => n.SummarizeAsync(It.IsAny<NlSummarizeRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.BadRequest, "找不到欄位"));
            var (_, tool) = await SetupAsync(Page());

            var result = await tool.RunAsync(Input(new { question = "總計" }), Ctx, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Contains("找不到欄位", result.Content);
            Assert.Null(result.Card);
        }

        [Fact]
        public async Task DescribeData_OnAnUploadedFile_ListsItsColumns_AndTheLatestDateOfItsDateColumns()
        {
            _sessions.Setup(s => s.GetSchema("s-1")).Returns(ServiceResult.Ok(new SessionSchemaDto(
            [
                new SessionFieldDefDto { Name = "日期", DataType = "datetime", CanonicalType = "DateTime" },
                new SessionFieldDefDto { Name = "地區", DataType = "nvarchar", CanonicalType = "String" },
            ])));
            _sessions.Setup(s => s.GetMaxDate("s-1", "日期")).Returns(ServiceResult.Ok(new SessionMaxDateDto("2026-09-30T00:00:00")));
            var (_, tool) = await SetupAsync(Page(("sessionId", "s-1")), DescribeDataTool.Name);

            var result = await tool.RunAsync(Input(new { }), Ctx, CancellationToken.None);

            Assert.False(result.IsError);
            var content = JsonDocument.Parse(result.Content).RootElement;
            Assert.Equal(new[] { "日期", "地區" }, content.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
            Assert.Equal("String", content.GetProperty("columns")[1].GetProperty("type").GetString());
            Assert.Equal("2026-09-30", content.GetProperty("latestDates").GetProperty("日期").GetString());
            _usage.Verify(u => u.TryConsumeAsync(It.IsAny<string>(), It.IsAny<UsageFeature>(), It.IsAny<ulong>()), Times.Never);
        }

        [Fact]
        public async Task DescribeData_ThatCannotReadTheSource_TellsTheModelWhy()
        {
            _queries.Setup(q => q.GetSchemaAsync(It.IsAny<QueryContextDto?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.NotFound, "找不到資料源"));
            var (_, tool) = await SetupAsync(Page(("sourceId", Source.ToString())), DescribeDataTool.Name);

            var result = await tool.RunAsync(Input(new { }), Ctx, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Contains("找不到資料源", result.Content);
        }

        // ── saving a chart to a dashboard ───────────────────────────────────────────────────────

        private static readonly Guid Board = Guid.Parse("99999999-0000-0000-0000-000000000001");

        private async Task<string> ChartIdAsync(params (string Key, string Value)[] page)
        {
            var (_, ask) = await SetupAsync(Page(page));
            var result = await ask.RunAsync(Input(new { question = "每月營收", chart = "line", title = "每月營收" }), Ctx, CancellationToken.None);
            return JsonDocument.Parse(result.Content).RootElement.GetProperty("chartId").GetString()!;
        }

        private void Dashboards(params DashboardSummaryDto[] owned)
        {
            _dashboards.Setup(d => d.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(owned.ToList()));
            _dashboards.Setup(d => d.CreateAsync(It.IsAny<CreateDashboardRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDashboardRequest r, CancellationToken _) => ServiceResult.Ok(new DashboardSummaryDto
                {
                    Id = Board, Name = r.Name, Visibility = default, IsOwner = true,
                }));
            _dashboards.Setup(d => d.AddWidgetAsync(It.IsAny<Guid>(), It.IsAny<AddWidgetRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Ok(new DashboardWidgetDto { Id = Guid.NewGuid(), VizType = "line", Plan = default }));
        }

        [Fact]
        public async Task AChartAskDataShowed_IsSavedToANewDashboard_WithItsCheckedPlanAndThePagesSource()
        {
            var chartId = await ChartIdAsync(("sourceId", Source.ToString()), ("tableName", "dbo.Orders"));
            Dashboards();
            var (_, save) = await SetupAsync(Page(), SaveToDashboardTool.Name);

            var result = await save.RunAsync(Input(new { chartId, dashboard = "業績" }), Ctx, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("已建立 dashboard「業績」", result.Content);
            _dashboards.Verify(d => d.CreateAsync(It.Is<CreateDashboardRequest>(r => r.Name == "業績"), It.IsAny<CancellationToken>()));
            _dashboards.Verify(d => d.AddWidgetAsync(Board, It.Is<AddWidgetRequest>(w =>
                w.VizType == "line" && w.Title == "每月營收" &&
                w.Plan.GetProperty("context").GetProperty("sourceId").GetString() == Source.ToString() &&
                w.Plan.GetProperty("measures")[0].GetProperty("fieldName").GetString() == "Amount"), It.IsAny<CancellationToken>()));
            Assert.Equal($"/dashboards/{Board}", JsonSerializer.SerializeToElement(result.Card).GetProperty("href").GetString());
            // Not asked to open it: the link card is all.
            Assert.Null(result.Navigate);
        }

        [Fact]
        public async Task AskedToOpenIt_TheSavedDashboardIsWhereThePageGoes_AndTheCardSaysSo()
        {
            var chartId = await ChartIdAsync();
            Dashboards();
            var (_, save) = await SetupAsync(Page(), SaveToDashboardTool.Name);
            var input = Input(new { chartId, dashboard = "業績", open = true });

            var result = await save.RunAsync(input, Ctx, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Equal($"/dashboards/{Board}", result.Navigate);
            Assert.Equal("把「每月營收」存到 dashboard「業績」，存好後打開它", ((IDescribesCall)save).Describe(input, Ctx));
        }

        [Fact]
        public async Task ASaveThatFails_GoesNowhere()
        {
            var chartId = await ChartIdAsync();
            Dashboards();
            _dashboards.Setup(d => d.AddWidgetAsync(It.IsAny<Guid>(), It.IsAny<AddWidgetRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ServiceResult<DashboardWidgetDto>)ServiceResult.Fail(ErrorCodes.InternalServerError, "x"));
            var (_, save) = await SetupAsync(Page(), SaveToDashboardTool.Name);

            var result = await save.RunAsync(Input(new { chartId, dashboard = "業績", open = true }), Ctx, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Null(result.Navigate);
        }

        [Fact]
        public async Task ADashboardOfThatName_TheReaderOwns_IsTheOneUsed()
        {
            var chartId = await ChartIdAsync();
            var mine = Guid.NewGuid();
            Dashboards(
                new DashboardSummaryDto { Id = Guid.NewGuid(), Name = "業績", Visibility = default, IsOwner = false },
                new DashboardSummaryDto { Id = mine, Name = "業績", Visibility = default, IsOwner = true });
            var (_, save) = await SetupAsync(Page(), SaveToDashboardTool.Name);

            await save.RunAsync(Input(new { chartId, dashboard = "業績", title = "月營收" }), Ctx, CancellationToken.None);

            _dashboards.Verify(d => d.CreateAsync(It.IsAny<CreateDashboardRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            _dashboards.Verify(d => d.AddWidgetAsync(mine, It.Is<AddWidgetRequest>(w => w.Title == "月營收"), It.IsAny<CancellationToken>()));
        }

        [Fact]
        public async Task AChartOfAnotherConversation_OrOfAnUploadedFile_CannotBeSaved()
        {
            var chartId = await ChartIdAsync();
            var (_, save) = await SetupAsync(Page(), SaveToDashboardTool.Name);

            var other = await save.RunAsync(Input(new { chartId, dashboard = "業績" }), new AiToolContext("u1", "conv-2"), CancellationToken.None);
            Assert.True(other.IsError);

            var (_, ask) = await SetupAsync(Page(("sessionId", "s-1")));
            var uploaded = await ask.RunAsync(Input(new { question = "總計" }), Ctx, CancellationToken.None);
            Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(uploaded.Content).RootElement.GetProperty("chartId").ValueKind);
            _dashboards.VerifyNoOtherCalls();
        }

        [Fact]
        public void AnUnknownChart_FallsBackToTheDefault_AndEveryChartIsOneTheClientDraws()
        {
            // The client's viz ids (DataAnalysis/viz/capabilities.tsx); a card with another is dropped there.
            string[] client = ["table", "kpi", "column", "bar", "line", "pie", "donut", "stackedColumn", "stackedBar",
                "treemap", "sunburst", "tree", "waterfall", "sankey"];
            Assert.All(AskDataTool.Charts, c => Assert.Contains(c, client));
            Assert.Contains(AskDataTool.DefaultChart, AskDataTool.Charts);
        }
    }
}
