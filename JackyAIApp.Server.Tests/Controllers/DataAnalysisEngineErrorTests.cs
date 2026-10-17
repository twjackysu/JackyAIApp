using System.Security.Claims;
using DataAnalysisLibrary.Core.Abstractions;
using DataAnalysisLibrary.Core.Base;
using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Enums;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// A failed engine query's driver text (SqlException / Kusto messages: server names, objects, SQL)
    /// reaches only the log for the default source, which is the app's own database behind anonymous
    /// endpoints. A custom source's owner still sees it, to debug their own database.
    /// </summary>
    public class DataAnalysisEngineErrorTests
    {
        private const string DriverText =
            "Login failed for user 'jackyai_app'. Server tcp:jackyai.database.windows.net, object dbo.Orders";

        private sealed class NamedBuilder<T> : IQueryBuilder<T> where T : QueryRequestBase
        {
            public string Build(T request) => typeof(T).Name;
        }

        private sealed class FakeExecutor : IQueryExecutor
        {
            // The schema read succeeds (so queries get past the field whitelist); everything else fails.
            public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecuteAsync(string queryString, CancellationToken cancellationToken = default) =>
                queryString == nameof(FieldListRequest)
                    ? Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
                        [new Dictionary<string, object?> { ["Name"] = "Region", ["DataType"] = "nvarchar" },
                         new Dictionary<string, object?> { ["Name"] = "Revenue", ["DataType"] = "decimal" }])
                    : throw new InvalidOperationException(DriverText);
        }

        private sealed class FakeFactory(string sourceKey) : DataSourceFactoryBase
        {
            public override string SourceKey => sourceKey;
            public override IQueryBuilder<DetailsRequest> CreateDetailsBuilder() => new NamedBuilder<DetailsRequest>();
            public override IQueryBuilder<SummarizeRequest> CreateSummarizeBuilder() => new NamedBuilder<SummarizeRequest>();
            public override IQueryBuilder<FieldListRequest> CreateFieldListBuilder() => new NamedBuilder<FieldListRequest>();
            public override IQueryBuilder<MaxDateRequest> CreateMaxDateBuilder() => new NamedBuilder<MaxDateRequest>();
            protected override IQueryExecutor CreateExecutorCore() => new FakeExecutor();
        }

        private static DataAnalysisEngine FailingEngine(string sourceKey = "mssql") =>
            new DataAnalysisEngineBuilder().AddSource(new FakeFactory(sourceKey)).Build();

        private static Mock<IMyResponseFactory> ResponseFactory()
        {
            var factory = new Mock<IMyResponseFactory>();
            factory.Setup(f => f.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                   .Returns((ErrorCodes code, string message) => new ObjectResult(message) { StatusCode = (int)code / 100 % 1000 });
            factory.Setup(f => f.CreateOKResponse(It.IsAny<object>()))
                   .Returns((object data) => new OkObjectResult(data));
            return factory;
        }

        private static (int Status, string Message) Error(IActionResult result)
        {
            var error = Assert.IsType<ObjectResult>(result);
            return (error.StatusCode!.Value, Assert.IsType<string>(error.Value));
        }

        private static AzureSQLDBContext Db() =>
            new(new DbContextOptionsBuilder<AzureSQLDBContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static HttpContext As(string? userId) => new DefaultHttpContext
        {
            User = userId is null
                ? new ClaimsPrincipal(new ClaimsIdentity())
                : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Cookies")),
        };

        private static DataAnalysisController Controller(
            AzureSQLDBContext? db = null, IDataSourceConfigService? configService = null, string? userId = null) =>
            new(NullLogger<DataAnalysisController>.Instance, ResponseFactory().Object,
                new DataSourceResolver(FailingEngine(), db!, configService!, NullLogger<DataSourceResolver>.Instance),
                dbContext: db!, configService: configService!, sessionStore: null!,
                openAIPromptService: null!, usageLimitService: null!, promptLoader: null!, nlPlanner: null!,
                cache: new MemoryCache(new MemoryCacheOptions()))
            {
                ControllerContext = new ControllerContext { HttpContext = As(userId) },
            };

        private static SummarizeRequestDto Plan(Guid? sourceId = null) => new()
        {
            ViewBy   = [new GroupFieldDto { FieldName = "Region" }],
            Measures = [new MeasureFieldDto { FieldName = "Revenue", Aggregation = AggregationType.Sum }],
            Context  = sourceId is null ? null : new QueryContextDto { SourceId = sourceId, TableName = "dbo.Sales" },
        };

        // ── Default source ───────────────────────────────────────────────────

        [Fact]
        public async Task DefaultSource_Summarize_Failure_ReturnsGenericTextOnly()
        {
            var (status, message) = Error(await Controller().Summarize(Plan()));

            Assert.Equal(500, status);
            Assert.Equal("Summarize failed.", message);
            Assert.DoesNotContain("jackyai", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DefaultSource_Details_Failure_ReturnsGenericTextOnly()
        {
            var (status, message) = Error(await Controller().Details(new DetailsRequestDto { SelectFields = ["Region"] }));

            Assert.Equal(500, status);
            Assert.Equal("Details failed.", message);
        }

        [Fact]
        public async Task DefaultSource_Export_Failure_ReturnsGenericTextOnly()
        {
            var controller = Controller();
            controller.ControllerContext.HttpContext.Response.Body = new MemoryStream();

            var (status, message) = Error(await controller.ExportDetails(new DetailsRequestDto()));

            Assert.Equal(500, status);
            Assert.Equal("Export failed.", message);
        }

        [Fact]
        public async Task DefaultSource_MaxDate_Failure_ReturnsGenericTextOnly()
        {
            var (_, message) = Error(await Controller().GetMaxDate(dateField: "Region"));

            Assert.Equal("MaxDate query failed.", message);
        }

        // ── Custom source ────────────────────────────────────────────────────

        private static async Task<(AzureSQLDBContext Db, Guid SourceId)> OwnedSource()
        {
            var db = Db();
            var id = Guid.NewGuid();
            db.DataSourceConfigs.Add(new DataSourceConfig
            {
                Id = id, UserId = "owner", Name = "Mine", ConnectorType = DataSourceConnectorType.MsSql,
                ConfigJson = "encrypted", CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            return (db, id);
        }

        [Fact]
        public async Task CustomSource_Failure_ShowsTheOwnerTheDriverMessage()
        {
            var (db, id) = await OwnedSource();
            var config = new Mock<IDataSourceConfigService>();
            config.Setup(c => c.BuildEngine(It.IsAny<DataSourceConfig>())).Returns(() => FailingEngine());
            config.Setup(c => c.SourceKey(It.IsAny<DataSourceConnectorType>())).Returns("mssql");

            var (status, message) = Error(await Controller(db, config.Object, userId: "owner").Summarize(Plan(id)));

            Assert.Equal(500, status);
            Assert.Equal(DriverText, message);
        }

        [Fact]
        public async Task CustomSource_RefusedStoredConfig_Is400WithTheReason()
        {
            var (db, id) = await OwnedSource();
            var config = new Mock<IDataSourceConfigService>();
            config.Setup(c => c.BuildEngine(It.IsAny<DataSourceConfig>()))
                  .Throws(new DataSourceConfigException("Integrated Security is not allowed; sign in with a User ID and Password."));

            var (status, message) = Error(await Controller(db, config.Object, userId: "owner").Summarize(Plan(id)));

            Assert.Equal(400, status);
            Assert.StartsWith("Integrated Security is not allowed", message);
        }

        [Fact]
        public async Task CreateSource_RefusedConfig_Is400AndNothingIsSaved()
        {
            var db = Db();
            var config = new Mock<IDataSourceConfigService>();
            config.Setup(c => c.SerializeAndEncrypt(It.IsAny<object>(), It.IsAny<DataSourceConnectorType>()))
                  .Throws(new DataSourceConfigException("Authentication=ActiveDirectoryManagedIdentity is not allowed."));

            var result = await Controller(db, config.Object, userId: "owner").CreateSource(new SaveDataSourceConfigDto
            {
                Name = "Prod", ConnectorType = DataSourceConnectorType.MsSql, Config = new { },
            });

            Assert.Equal(400, Error(result).Status);
            Assert.Empty(db.DataSourceConfigs);
        }

        // ── Dashboard widget (anonymous viewers on an "Anyone" dashboard) ────

        [Fact]
        public async Task WidgetData_DefaultSourceFailure_ReturnsGenericTextOnly()
        {
            var db = Db();
            var dash = new Dashboard { Id = Guid.NewGuid(), UserId = "owner", Name = "Public board", Visibility = "Anyone" };
            var widget = new DashboardWidget
            {
                Id = Guid.NewGuid(), DashboardId = dash.Id, VizType = "bar",
                PlanJson = """{"viewBy":[{"fieldName":"Region"}],"measures":[{"fieldName":"Revenue","aggregation":"Sum"}]}""",
            };
            db.Dashboards.Add(dash);
            db.DashboardWidgets.Add(widget);
            await db.SaveChangesAsync();

            var controller = new DashboardController(
                NullLogger<DashboardController>.Instance, ResponseFactory().Object, db,
                new DataSourceResolver(FailingEngine(), db, null!, NullLogger<DataSourceResolver>.Instance),
                configService: null!, cache: new MemoryCache(new MemoryCacheOptions()))
            {
                ControllerContext = new ControllerContext { HttpContext = As(null) },
            };

            var (status, message) = Error(await controller.WidgetData(dash.Id, widget.Id));

            Assert.Equal(500, status);
            Assert.Equal("Query failed.", message);
        }
    }
}
