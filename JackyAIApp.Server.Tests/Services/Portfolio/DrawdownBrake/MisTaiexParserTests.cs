using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Tests.TestHelpers;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    public class MisTaiexParserTests
    {
        private static MisIndexQuote? Parse(string json, out string? rejection) => MisTaiexParser.Parse(json, out rejection);

        [Fact]
        public void AWholeReading_IsParsed()
        {
            var quote = Parse(MisTaiexSamples.Json(), out var rejection);

            Assert.Null(rejection);
            Assert.Equal(new DateOnly(2026, 10, 8), quote!.TradeDate);
            Assert.Equal(23100.50m, quote.Last);
            Assert.Equal(23000.00m, quote.PreviousClose);
            Assert.Equal(MisTaiexSamples.DefaultQuotedAt, quote.QuotedAt);
            Assert.Equal(TimeSpan.FromHours(8), quote.QuotedAt.Offset);
        }

        [Theory]
        [InlineData("-")]       // MIS's "no trade yet"
        [InlineData("")]
        [InlineData("0")]
        [InlineData("0.00")]
        [InlineData("-23100.5")]
        [InlineData("23,100.50")] // separators are not how MIS writes numbers; unknown formats are not guessed at
        [InlineData("abc")]
        public void ALastThatIsNotAPositivePlainNumber_IsNoReading(string z)
        {
            Assert.Null(Parse(MisTaiexSamples.Json(z: z), out var rejection));
            Assert.Contains("z (last)", rejection);
        }

        [Fact]
        public void AMissingLast_IsNoReading()
        {
            Assert.Null(Parse(MisTaiexSamples.Json(z: null), out var rejection));
            Assert.Contains("z (last)", rejection);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("-")]
        [InlineData("0")]
        public void APreviousCloseThatIsMissingOrNotPositive_IsNoReading(string? y)
        {
            Assert.Null(Parse(MisTaiexSamples.Json(y: y), out var rejection));
            Assert.Contains("y (previous close)", rejection);
        }

        [Fact]
        public void ValuesThatAreNotStrings_AreNotRead()
        {
            // MIS writes every value as a string; a number there means the format changed.
            const string json = "{\"msgArray\":[{\"c\":\"t00\",\"z\":23100.5,\"y\":23000.0,\"d\":\"20261008\",\"t\":\"13:30:00\"}]}";

            Assert.Null(Parse(json, out var rejection));
            Assert.Contains("z (last)", rejection);
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("<html><body>Forbidden</body></html>")]
        [InlineData("")]
        public void NotJson_IsNoReading(string body)
        {
            Assert.Null(Parse(body, out var rejection));
            Assert.Equal("response is not JSON", rejection);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("[]")]
        [InlineData("{\"msgArray\":\"x\"}")]
        [InlineData("{\"msgArray\":null}")]
        public void NoMsgArray_IsNoReading(string body)
        {
            Assert.Null(Parse(body, out var rejection));
            Assert.Equal("no msgArray", rejection);
        }

        [Fact]
        public void AnEmptyMsgArray_IsNoReading()
        {
            // What MIS answers for a code it does not know, or without the session it wants.
            Assert.Null(Parse("{\"msgArray\":[],\"rtcode\":\"0000\"}", out var rejection));
            Assert.Equal("msgArray is empty", rejection);
        }

        [Fact]
        public void AnEntryThatIsNotAnObject_IsNoReading()
        {
            Assert.Null(Parse("{\"msgArray\":[\"x\"]}", out var rejection));
            Assert.Contains("not an object", rejection);
        }

        [Fact]
        public void AnotherInstrument_IsNoReading()
        {
            Assert.Null(Parse(MisTaiexSamples.Json(c: "2330", ch: "2330.tw"), out var rejection));
            Assert.Contains("is not t00", rejection);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("2330", null)]
        [InlineData(null, "2330.tw")]
        public void AnEntryThatDoesNotSayItIsTheIndex_IsNoReading(string? c, string? ch)
        {
            Assert.Null(Parse(MisTaiexSamples.Json(c: c, ch: ch), out _));
        }

        [Theory]
        [InlineData("t00", null)]
        [InlineData(null, "t00.tw")]
        [InlineData("T00", "T00.TW")]
        public void EitherTheCodeOrTheChannel_IsEnoughToSayItIsTheIndex(string? c, string? ch)
        {
            Assert.NotNull(Parse(MisTaiexSamples.Json(c: c, ch: ch), out _));
        }

        [Theory]
        [InlineData("26450.00", false)] // +15.0% from 23000 → at the limit, still believed (the check is strictly greater)
        [InlineData("26450.01", true)]
        [InlineData("19550.00", false)] // −15.0%
        [InlineData("19549.99", true)]
        public void AMoveBeyondFifteenPercent_IsBadData_NotNews(string z, bool rejected)
        {
            var quote = Parse(MisTaiexSamples.Json(z: z, y: "23000.00"), out var rejection);

            if (rejected)
            {
                Assert.Null(quote);
                Assert.Contains("move", rejection);
            }
            else
            {
                Assert.NotNull(quote);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("2026-10-08")]
        [InlineData("1008")]
        [InlineData("20261308")]
        [InlineData("abcdefgh")]
        public void ADateThatIsNotYyyyMMdd_IsNoReading(string? d)
        {
            Assert.Null(Parse(MisTaiexSamples.Json(d: d), out var rejection));
            Assert.Contains("d (date)", rejection);
        }

        [Theory]
        [InlineData("00010101", "00:30:00")] // a date at the very start of the calendar: no UTC+8 time on it
        [InlineData("99991231", "23:59:59")]
        public void ADateAtTheEdgeOfTheCalendar_IsNoReading_NotAnException(string d, string t)
        {
            var quote = Parse(MisTaiexSamples.Json(d: d, t: t, tlong: null), out var rejection);

            // 9999-12-31 is representable (it just is not today); 0001-01-01 must be turned away, not thrown on.
            if (d == "00010101") Assert.Null(quote);
            else Assert.NotNull(quote);
            Assert.Equal(quote is null, rejection is not null);
        }

        [Fact]
        public void TheTime_IsTheTimeOfDayOnTheTradeDate_InTaipei()
        {
            var quote = Parse(MisTaiexSamples.Json(t: "09:05:07", tlong: null), out _);

            Assert.Equal(new DateTimeOffset(2026, 10, 8, 9, 5, 7, TimeSpan.FromHours(8)), quote!.QuotedAt);
        }

        [Fact]
        public void WithoutAUsableTime_TheEpochMilliseconds_AreUsed()
        {
            var at = new DateTimeOffset(2026, 10, 8, 10, 15, 30, TimeSpan.FromHours(8));

            var quote = Parse(MisTaiexSamples.Json(t: "-", tlong: at.ToUnixTimeMilliseconds().ToString()), out _);

            Assert.Equal(at, quote!.QuotedAt);
            Assert.Equal(TimeSpan.FromHours(8), quote.QuotedAt.Offset);
        }

        [Fact]
        public void EpochMillisecondsOnAnotherDay_AreNotTheTradeDatesTime()
        {
            var yesterday = new DateTimeOffset(2026, 10, 7, 13, 30, 0, TimeSpan.FromHours(8));

            Assert.Null(Parse(MisTaiexSamples.Json(t: null, tlong: yesterday.ToUnixTimeMilliseconds().ToString()), out var rejection));
            Assert.Contains("time", rejection);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("13:30", null)]       // no seconds: not the format
        [InlineData("25:00:00", null)]
        [InlineData(null, "abc")]
        [InlineData(null, "-1")]
        [InlineData(null, "99999999999999999999")]
        [InlineData(null, "253402300798000")]  // 9999-12-31: representable, but local time would overflow
        public void NoTimeThatCanBeRead_IsNoReading(string? t, string? tlong)
        {
            Assert.Null(Parse(MisTaiexSamples.Json(t: t, tlong: tlong), out var rejection));
            Assert.Contains("time", rejection);
        }
    }
}
