using JackyAIApp.Server.Services.DataAnalysis;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// Reported: 「最新一個月各地區的營收」 with data up to 2026-10-13 filtered 2026-09-01 ~ 2026-10-13.
    /// The ranges are now computed here and handed to the LLM, so it copies instead of calculating.
    /// </summary>
    public class NlDateRangesTests
    {
        [Fact]
        public void LatestMonth_IsTheCalendarMonthOfTheLatestDate()
        {
            var block = NlDateRanges.Describe("OrderDate", "2026-10-13");

            Assert.StartsWith("- OrderDate: latest = 2026-10-13", block);
            Assert.Contains("latest month (最新/最近一個月/本月/這個月): 2026-10-01 ~ 2026-10-13", block);
            Assert.Contains("previous month (上個月): 2026-09-01 ~ 2026-09-30", block);
            Assert.Contains("latest quarter (最新一季/本季): 2026-10-01 ~ 2026-10-13", block);
            Assert.Contains("previous quarter (上一季/上季): 2026-07-01 ~ 2026-09-30", block);
            Assert.Contains("this year (今年): 2026-01-01 ~ 2026-10-13", block);
            Assert.Contains("last year (去年): 2025-01-01 ~ 2025-12-31", block);
        }

        [Fact]
        public void January_PreviousMonthAndQuarterFallInLastYear()
        {
            var block = NlDateRanges.Describe("D", "2026-01-05");

            Assert.Contains("previous month (上個月): 2025-12-01 ~ 2025-12-31", block);
            Assert.Contains("previous quarter (上一季/上季): 2025-10-01 ~ 2025-12-31", block);
        }

        [Fact]
        public void LeapYear_FebruaryEndsOnThe29th()
        {
            Assert.Contains("previous month (上個月): 2024-02-01 ~ 2024-02-29", NlDateRanges.Describe("D", "2024-03-10"));
        }

        /// <summary>A session data source reports its max date in round-trip ("o") form.</summary>
        [Theory]
        [InlineData("2026-10-13T00:00:00.0000000")]
        [InlineData("2026-10-13T23:30:00.0000000Z")]
        [InlineData("2026-10-13T23:30:00.0000000+08:00")]
        public void RoundTripValue_UsesItsWrittenDate(string latest)
        {
            var block = NlDateRanges.Describe("D", latest);

            Assert.StartsWith("- D: latest = 2026-10-13", block);
            Assert.Contains("latest month (最新/最近一個月/本月/這個月): 2026-10-01 ~ 2026-10-13", block);
        }

        [Fact]
        public void UnparseableValue_PassedThroughAsLatestOnly()
        {
            Assert.Equal("- D: latest = soon", NlDateRanges.Describe("D", "soon"));
        }
    }
}
