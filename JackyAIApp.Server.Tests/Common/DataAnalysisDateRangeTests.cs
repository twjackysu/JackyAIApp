using DataAnalysisLibrary.Core.Enums;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// The reported failure: <c>OrderDate Between ["2026-10-12", "2026-10-13"]</c> returned only the
    /// 12th. OrderDate carries a time, and "2026-10-13" compares as midnight, so every order placed
    /// later on the end day was dropped — and the NL planner always ends relative ranges ("最新一個月",
    /// "今年") on the data's latest date, so those answers silently lost their last day.
    /// </summary>
    public class DataAnalysisDateRangeTests
    {
        private static FilterDimensionDto Between(string from, string to) =>
            new() { FieldName = "OrderDate", Op = "Between", Values = [from, to] };

        [Fact]
        public void DateOnlyEnd_BecomesHalfOpenRangeThroughTheNextDay()
        {
            var expanded = DataAnalysisMapping.ExpandDateOnlyRangeEnds([Between("2026-10-12", "2026-10-13")]);

            Assert.Collection(expanded,
                f => { Assert.Equal("Between", f.Op); Assert.Equal(["2026-10-12", "2026-10-14"], f.Values); },
                f => { Assert.Equal("LessThan", f.Op); Assert.Equal(["2026-10-14"], f.Values); });
        }

        [Fact]
        public void DateOnlyEnd_RollsOverMonthAndYear()
        {
            var expanded = DataAnalysisMapping.ExpandDateOnlyRangeEnds([Between("2026-01-01", "2026-12-31")]);

            Assert.Equal(["2027-01-01"], expanded[1].Values);
        }

        [Theory]
        [InlineData("2026-10-13T12:00:00")] // explicit time: the caller meant it
        [InlineData("100")]                 // numeric range
        public void NonDateOnlyEnd_IsUnchanged(string to)
        {
            var filter = Between("0", to);

            Assert.Same(filter, Assert.Single(DataAnalysisMapping.ExpandDateOnlyRangeEnds([filter])));
        }

        [Fact]
        public void OtherOps_AreUnchanged()
        {
            var filter = new FilterDimensionDto { FieldName = "OrderDate", Op = "LessThan", Values = ["2026-10-13"] };

            Assert.Same(filter, Assert.Single(DataAnalysisMapping.ExpandDateOnlyRangeEnds([filter])));
        }

        [Fact]
        public void MapFilters_EmitsBothLibraryFilters()
        {
            var mapped = DataAnalysisMapping.MapFilters([Between("2026-10-12", "2026-10-13")]);

            Assert.Equal([FilterOperator.Between, FilterOperator.LessThan], mapped.Select(f => f.Op));
        }

        [Fact]
        public void SessionStore_IncludesTheWholeEndDay()
        {
            var store = new SessionDataStore(new MemoryCache(new MemoryCacheOptions()));
            var upload = store.Upload(new SessionUploadDto
            {
                Rows =
                [
                    new() { ["OrderDate"] = "2026-10-11T23:59:59", ["Revenue"] = "1" },
                    new() { ["OrderDate"] = "2026-10-12T10:00:00", ["Revenue"] = "1" },
                    new() { ["OrderDate"] = "2026-10-13T23:32:11", ["Revenue"] = "1" },
                    new() { ["OrderDate"] = "2026-10-14T00:00:00", ["Revenue"] = "1" },
                ],
            });
            var sessionId = ((SessionUploadResponseDto)upload.Data!).SessionId;

            var result = store.Details(sessionId, new DetailsRequestDto { Filters = [Between("2026-10-12", "2026-10-13")] });

            var totalCount = (int)result.Data!.GetType().GetProperty("totalCount")!.GetValue(result.Data)!;
            Assert.Equal(2, totalCount);
        }
    }
}
