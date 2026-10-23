using System.Globalization;
using System.Text;
using DataAnalysisLibrary.Core.Abstractions;
using DataAnalysisLibrary.Core.Base;
using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Tests.TestHelpers;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// The Details tab's CSV export: one query without paging, capped at MaxExportRows, with the
    /// real total in X-Total-Count so the client can say when the file was cut off.
    /// </summary>
    public class DataAnalysisExportTests
    {
        // ── CsvExportWriter ──────────────────────────────────────────────────

        [Theory]
        [InlineData(null, "")]
        [InlineData("plain", "plain")]
        [InlineData("a,b", "\"a,b\"")]
        [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [InlineData("two\nlines", "\"two\nlines\"")]
        [InlineData("=SUM(A1)", "'=SUM(A1)")]
        [InlineData("@cmd", "'@cmd")]
        [InlineData("+x", "'+x")]
        [InlineData("-5", "-5")]          // session uploads keep numbers as text — not a formula
        [InlineData("+3.2", "+3.2")]
        [InlineData(true, "true")]
        public void Cell_FormatsAndGuards(object? value, string expected) =>
            Assert.Equal(expected, CsvExportWriter.Cell(value));

        [Fact]
        public void Cell_NumbersAndDatesIgnoreTheCurrentCulture()
        {
            var saved = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");  // decimal comma
            try
            {
                Assert.Equal("1234.5", CsvExportWriter.Cell(1234.5m));
                Assert.Equal("-0.25", CsvExportWriter.Cell(-0.25d));
                Assert.Equal("2026-10-13", CsvExportWriter.Cell(new DateTime(2026, 10, 13)));
                Assert.Equal("2026-10-13 09:05:07", CsvExportWriter.Cell(new DateTime(2026, 10, 13, 9, 5, 7)));
                Assert.Equal("2026-10-13 09:05:07+08:00",
                    CsvExportWriter.Cell(new DateTimeOffset(2026, 10, 13, 9, 5, 7, TimeSpan.FromHours(8))));
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Fact]
        public async Task WriteAsync_WritesBomHeaderAndCrlfRows()
        {
            using var stream = new MemoryStream();
            await CsvExportWriter.WriteAsync(stream, ["Name", "Qty"],
                [new object?[] { "A, Inc.", 3 }, new object?[] { null, 4 }]);

            var bytes = stream.ToArray();
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Equal("Name,Qty\r\n\"A, Inc.\",3\r\n,4\r\n", Encoding.UTF8.GetString(bytes[3..]));
        }

        // ── Database source (default engine) ─────────────────────────────────

        /// <summary>Each builder returns its request type's name, so the executor can answer per query.</summary>
        private sealed class NamedBuilder<T> : IQueryBuilder<T> where T : QueryRequestBase
        {
            public List<T> Requests { get; } = [];
            public string Build(T request) { Requests.Add(request); return typeof(T).Name; }
        }

        private sealed class FakeExecutor(Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> answer) : IQueryExecutor
        {
            public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecuteAsync(string queryString, CancellationToken cancellationToken = default)
                => Task.FromResult(answer(queryString));
        }

        private sealed class FakeFactory(IQueryExecutor executor, NamedBuilder<DetailsRequest> details) : DataSourceFactoryBase
        {
            public override string SourceKey => "mssql";
            public override IQueryBuilder<DetailsRequest> CreateDetailsBuilder() => details;
            public override IQueryBuilder<SummarizeRequest> CreateSummarizeBuilder() => new NamedBuilder<SummarizeRequest>();
            public override IQueryBuilder<FieldListRequest> CreateFieldListBuilder() => new NamedBuilder<FieldListRequest>();
            protected override IQueryExecutor CreateExecutorCore() => executor;
        }

        private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(params (string Region, decimal Revenue)[] rows) =>
            rows.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["Region"] = r.Region,
                ["Revenue"] = r.Revenue,
            }).ToList();

        private static (DataAnalysisController Controller, HttpResponse Response) Controller(
            DataAnalysisEngine defaultEngine, ISessionDataStore? sessions = null)
        {
            var controller = new DataAnalysisControllerBuilder { DefaultEngine = defaultEngine, Sessions = sessions }.Build();
            return (controller, controller.HttpContext.Response);
        }

        private static string Body(HttpResponse response)
        {
            response.Body.Position = 0;
            return new StreamReader(response.Body, Encoding.UTF8).ReadToEnd();  // drops the BOM
        }

        private static DataAnalysisEngine Engine(
            Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> answer, NamedBuilder<DetailsRequest>? details = null) =>
            new DataAnalysisEngineBuilder().AddSource(new FakeFactory(new FakeExecutor(answer), details ?? new())).Build();

        [Fact]
        public async Task ExportDetails_DefaultSource_WritesEveryRowInOneQueryWithoutPaging()
        {
            var details = new NamedBuilder<DetailsRequest>();
            // The ORDER BY column has to be in the table's schema to get past the field whitelist.
            var engine = Engine(q => q == nameof(FieldListRequest)
                ? [new Dictionary<string, object?> { ["Name"] = "OrderDate", ["DataType"] = "datetime2" }]
                : Rows(("EMEA", 10.5m), ("APAC", 7m)), details);
            var (controller, response) = Controller(engine);

            var result = await controller.ExportDetails(new DetailsRequestDto
            {
                Skip = 500, Take = 10,   // ignored: an export is never paged
                OrderBy = [new SortFieldDto { FieldName = "OrderDate", Descending = true }],
            });

            Assert.IsType<EmptyResult>(result);
            var request = Assert.Single(details.Requests);
            Assert.Equal("dbo.Orders", request.TableName);   // default source stays pinned
            Assert.Equal(0, request.Skip);
            Assert.Equal(DataAnalysisQueryService.MaxExportRows, request.Take);
            Assert.Equal("OrderDate", Assert.Single(request.OrderBy).FieldName);

            Assert.StartsWith("text/csv", response.ContentType);
            Assert.Contains("attachment", response.Headers.ContentDisposition.ToString());
            Assert.Contains("dbo.Orders_", response.Headers.ContentDisposition.ToString());
            Assert.Equal("2", response.Headers["X-Exported-Count"]);
            Assert.Equal("2", response.Headers["X-Total-Count"]);
            Assert.Equal("Region,Revenue\r\nEMEA,10.5\r\nAPAC,7\r\n", Body(response));
        }

        [Fact]
        public async Task ExportDetails_MoreRowsThanTheCap_ReportsTheRealTotal()
        {
            var full = Enumerable.Range(0, DataAnalysisQueryService.MaxExportRows)
                .Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["Id"] = i })
                .ToList();
            // A full page makes the library count separately (through the Summarize builder).
            var engine = Engine(q => q == nameof(SummarizeRequest)
                ? [new Dictionary<string, object?> { ["__TotalCount"] = 123_456L }]
                : full);
            var (controller, response) = Controller(engine);

            await controller.ExportDetails(new DetailsRequestDto());

            Assert.Equal("100000", response.Headers["X-Exported-Count"]);
            Assert.Equal("123456", response.Headers["X-Total-Count"]);
            var lines = Body(response).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(DataAnalysisQueryService.MaxExportRows + 1, lines.Length);   // + header
        }

        [Fact]
        public async Task ExportDetails_NothingMatches_StillWritesTheHeaderRow()
        {
            var engine = Engine(q => q == nameof(FieldListRequest)
                ? [new Dictionary<string, object?> { ["Name"] = "Region", ["DataType"] = "nvarchar" },
                   new Dictionary<string, object?> { ["Name"] = "Revenue", ["DataType"] = "decimal" }]
                : []);
            var (controller, response) = Controller(engine);

            await controller.ExportDetails(new DetailsRequestDto
            {
                Filters = [new FilterDimensionDto { FieldName = "Region", Values = ["Nowhere"] }],
            });

            Assert.Equal("0", response.Headers["X-Total-Count"]);
            Assert.Equal("Region,Revenue\r\n", Body(response));
        }

        [Fact]
        public async Task ExportDetails_QueryFails_ReturnsErrorWithoutAFile()
        {
            var engine = Engine(_ => throw new InvalidOperationException("boom"));
            var (controller, response) = Controller(engine);

            var result = await controller.ExportDetails(new DetailsRequestDto());

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
            Assert.Null(response.ContentType);
        }

        // ── Session uploads ──────────────────────────────────────────────────

        private static (SessionDataStore Store, string SessionId) Session()
        {
            var store = new SessionDataStore(new MemoryCache(new MemoryCacheOptions()));
            var upload = store.Upload(new SessionUploadDto
            {
                Name = "Sales 2026",
                Rows =
                [
                    new() { ["Region"] = "EMEA", ["Amount"] = "-5",  ["Day"] = "2026-01-02" },
                    new() { ["Region"] = "APAC", ["Amount"] = "20",  ["Day"] = "2026-01-03" },
                    new() { ["Region"] = "EMEA", ["Amount"] = "7.5", ["Day"] = "2026-01-04" },
                ],
            });
            return (store, upload.Data!.SessionId);
        }

        [Fact]
        public void SessionExport_FiltersSortsAndCaps_ButCountsEveryMatch()
        {
            var (store, id) = Session();

            var result = store.Export(id, new DetailsRequestDto
            {
                Filters = [new FilterDimensionDto { FieldName = "Region", Values = ["EMEA"] }],
                OrderBy = [new SortFieldDto { FieldName = "Day", Descending = true }],
            }, maxRows: 1);

            var export = Assert.IsType<SessionExport>(result.Data);
            Assert.Equal(["Region", "Amount", "Day"], export.Headers);
            Assert.Equal("2026-01-04", Assert.Single(export.Rows)["Day"]);   // newest EMEA row only
            Assert.Equal(2, export.TotalCount);
        }

        [Fact]
        public async Task SessionExportDetails_WritesTheSessionAsCsv()
        {
            var (store, id) = Session();
            var (controller, response) = Controller(Engine(_ => []), store);

            var result = await controller.SessionExportDetails(id, new DetailsRequestDto
            {
                OrderBy = [new SortFieldDto { FieldName = "Day", Descending = false }],
            });

            Assert.IsType<EmptyResult>(result);
            Assert.Contains("Sales_2026_", response.Headers.ContentDisposition.ToString());
            Assert.Equal("3", response.Headers["X-Total-Count"]);
            Assert.Equal("Region,Amount,Day\r\nEMEA,-5,2026-01-02\r\nAPAC,20,2026-01-03\r\nEMEA,7.5,2026-01-04\r\n", Body(response));
        }

        [Fact]
        public async Task SessionExportDetails_UnknownSession_IsNotFound()
        {
            var (store, _) = Session();
            var (controller, _) = Controller(Engine(_ => []), store);

            var result = await controller.SessionExportDetails("missing", new DetailsRequestDto());

            Assert.Equal(404, Assert.IsType<ObjectResult>(result).StatusCode);
        }
    }
}
