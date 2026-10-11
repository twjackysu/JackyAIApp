using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.Extensions.Logging.Abstractions;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    /// <summary>
    /// MFI94U month parsing. Most samples are hand-written to the layout Issue #431 §6 describes
    /// (民國 dates, thousands separators, "--" for no data). <see cref="Parse_TheLiveResponse_OfSeptember2026"/>
    /// is the start of a real response, copied from the 2026-10-10 contract-test run, which is what
    /// showed the date header to be "日　期" with a full-width space. The layout stays checked
    /// against the live API by LiveContracts/TaiexContractTests.
    /// </summary>
    public class TaiexTotalReturnParserTests
    {
        private static TaiexMonthResult Parse(string body, int year = 2026, int month = 10) =>
            TaiexTotalReturnParser.Parse(body, year, month, NullLogger.Instance);

        private static string Month(string fields, params string[] rows) =>
            "{\"stat\":\"OK\",\"title\":\"115年10月 發行量加權股價報酬指數\",\"fields\":" + fields +
            ",\"data\":[" + string.Join(",", rows) + "]}";

        private const string TwoColumns = "[\"日期\",\"發行量加權股價報酬指數\"]";

        [Fact]
        public void Parse_RocDatesAndThousandsSeparators()
        {
            var result = Parse(Month(TwoColumns, "[\"115/10/01\",\"41,234.56\"]", "[\"115/10/08\",\"1,050,000.1\"]"));

            Assert.True(result.Success);
            Assert.Equal(
                [new IndexClose(new DateOnly(2026, 10, 1), 41234.56m), new IndexClose(new DateOnly(2026, 10, 8), 1050000.1m)],
                result.Points);
        }

        [Fact]
        public void Parse_DoubleDash_IsNoData_AndTheRestOfTheMonthStillParses()
        {
            var result = Parse(Month(TwoColumns, "[\"115/10/01\",\"41,234.56\"]", "[\"115/10/02\",\"--\"]", "[\"115/10/05\",\"41,300.10\"]"));

            Assert.True(result.Success);
            Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5)], result.Points.Select(p => p.Date));
        }

        [Fact]
        public void Parse_ReturnsDaysAscending_EvenIfTheSourceIsNot()
        {
            var result = Parse(Month(TwoColumns, "[\"115/10/05\",\"2\"]", "[\"115/10/01\",\"1\"]"));

            Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5)], result.Points.Select(p => p.Date));
        }

        [Fact]
        public void Parse_FindsTheColumnsByHeaderNotPosition()
        {
            var fields = "[\"漲跌點數\",\"發行量加權股價指數\",\"發行量加權股價報酬指數\",\"日期\"]";
            var result = Parse(Month(fields, "[\"+10.5\",\"20,000.00\",\"41,234.56\",\"115/10/01\"]"));

            Assert.True(result.Success);
            Assert.Equal(new IndexClose(new DateOnly(2026, 10, 1), 41234.56m), Assert.Single(result.Points));
        }

        [Fact]
        public void Parse_TheLiveResponse_OfSeptember2026()
        {
            // Verbatim from the live API (first rows of the month), including the date header's
            // full-width space (U+3000) and the extra "title"/"date" members.
            const string live = "{\"stat\":\"OK\",\"title\":\"115年09月 發行量加權股價報酬指數\",\"date\":\"20260901\"," +
                "\"fields\":[\"日\u3000期\",\"發行量加權股價報酬指數\"]," +
                "\"data\":[[\"115/09/01\",\"108,395.72\"],[\"115/09/02\",\"106,591.66\"],[\"115/09/03\",\"105,884.91\"],[\"115/09/04\",\"107,486.11\"]]}";

            var result = Parse(live, 2026, 9);

            Assert.True(result.Success);
            Assert.Equal(
                [
                    new IndexClose(new DateOnly(2026, 9, 1), 108395.72m),
                    new IndexClose(new DateOnly(2026, 9, 2), 106591.66m),
                    new IndexClose(new DateOnly(2026, 9, 3), 105884.91m),
                    new IndexClose(new DateOnly(2026, 9, 4), 107486.11m),
                ],
                result.Points);
        }

        [Theory]
        [InlineData("日 期")]       // ASCII space
        [InlineData("日\u3000期")] // full-width space
        [InlineData(" 日期 ")]
        public void Parse_DateHeaderWhitespace_DoesNotMatter(string dateHeader)
        {
            var fields = "[\"" + dateHeader + "\",\"發行量加權股價報酬指數\"]";

            Assert.True(Parse(Month(fields, "[\"115/10/01\",\"41,234.56\"]")).Success);
        }

        [Theory]
        [InlineData("{\"stat\":\"很抱歉，沒有符合條件的資料!\"}")]
        [InlineData("{\"stat\":\"查詢日期小於93年1月1日，請重新查詢!\"}")]
        public void Parse_StatNotOk_IsAnEmptyMonth_NotAFailure(string body)
        {
            var result = Parse(body);

            Assert.True(result.Success);
            Assert.Empty(result.Points);
        }

        [Fact]
        public void Parse_OkWithNoRows_IsAnEmptyMonth()
        {
            var result = Parse(Month(TwoColumns));

            Assert.True(result.Success);
            Assert.Empty(result.Points);
        }

        [Fact]
        public void Parse_HtmlPage_IsAFailure()
        {
            // What a rate-limit or maintenance page looks like.
            Assert.False(Parse("<html><body>您的查詢過於頻繁，請稍後再試</body></html>").Success);
        }

        [Fact]
        public void Parse_NoTotalReturnColumn_IsAFailure()
        {
            Assert.False(Parse(Month("[\"日期\",\"發行量加權股價指數\"]", "[\"115/10/01\",\"20,000.00\"]")).Success);
        }

        [Fact]
        public void Parse_MoreThanOneTotalReturnColumn_IsAFailure()
        {
            var fields = "[\"日期\",\"發行量加權股價報酬指數\",\"報酬指數漲跌點數\"]";

            Assert.False(Parse(Month(fields, "[\"115/10/01\",\"41,234.56\",\"+10.0\"]")).Success);
        }

        [Fact]
        public void Parse_NoDateColumn_IsAFailure()
        {
            Assert.False(Parse(Month("[\"發行量加權股價報酬指數\"]", "[\"41,234.56\"]")).Success);
        }

        [Theory]
        [InlineData("[\"115/10/01\",\"abc\"]")]       // not a number
        [InlineData("[\"115/10/01\",\"0\"]")]         // an index can't be zero
        [InlineData("[\"115/10/01\",\"-5\"]")]
        [InlineData("[\"2026-10-01\",\"41,234.56\"]")] // not a 民國 date
        [InlineData("[\"115/10/32\",\"41,234.56\"]")]
        [InlineData("[\"115/09/30\",\"41,234.56\"]")]  // a day outside the requested month
        [InlineData("[\"115/10/01\"]")]                // short row
        public void Parse_UnreadableRow_FailsTheWholeMonth(string badRow)
        {
            var result = Parse(Month(TwoColumns, "[\"115/10/02\",\"41,000.00\"]", badRow));

            Assert.False(result.Success);
            Assert.Empty(result.Points); // nothing from a bad month is handed back
        }

        [Fact]
        public void Parse_SameDateTwice_FailsTheWholeMonth()
        {
            Assert.False(Parse(Month(TwoColumns, "[\"115/10/01\",\"1\"]", "[\"115/10/01\",\"2\"]")).Success);
        }

        [Fact]
        public void Parse_NoStat_IsAFailure() =>
            Assert.False(Parse("{\"fields\":[],\"data\":[]}").Success);

        [Fact]
        public void Parse_OkWithoutFieldsOrData_IsAFailure() =>
            Assert.False(Parse("{\"stat\":\"OK\"}").Success);

        [Theory]
        [InlineData("115/10/08", 2026, 10, 8)]
        [InlineData("99/01/04", 2010, 1, 4)]
        [InlineData("115/02/29", 0, 0, 0)] // 2026 is not a leap year
        [InlineData("115-10-08", 0, 0, 0)]
        [InlineData("", 0, 0, 0)]
        public void TryParseRocDate_Cases(string text, int year, int month, int day)
        {
            var ok = TaiexTotalReturnParser.TryParseRocDate(text, out var date);

            Assert.Equal(year != 0, ok);
            if (ok) Assert.Equal(new DateOnly(year, month, day), date);
        }
    }
}
