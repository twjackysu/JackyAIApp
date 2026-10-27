using System.Text;
using System.Text.Json;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// A session upload's rows are read with a limit on their size in memory (D-158): the body is only checked by
    /// its length before it is read, and tiny cells cost far more in memory than in JSON.
    /// </summary>
    public class SessionRowsJsonConverterTests
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private static SessionUploadDto Parse(string json) => JsonSerializer.Deserialize<SessionUploadDto>(json, Web)!;

        [Fact]
        public void Rows_AreReadAsBefore_ANullCellKept_AndARepeatedNameItsLastValue()
        {
            var dto = Parse("""{"name":"持股","rows":[{"代號":"2330","名稱":"台積電"},{"代號":null,"代號 ":"x","名稱":"a","名稱":"b"},{}]}""");

            Assert.Equal("持股", dto.Name);
            Assert.Equal(3, dto.Rows.Count);
            Assert.Equal("台積電", dto.Rows[0]["名稱"]);
            Assert.Null(dto.Rows[1]["代號"]);
            Assert.Equal("b", dto.Rows[1]["名稱"]);
            Assert.Empty(dto.Rows[2]);
        }

        [Fact]
        public void NoRows_StaysNull_ForTheStoreToRefuse()
        {
            Assert.Null(Parse("""{"rows":null}""").Rows);
        }

        [Theory]
        [InlineData("""{"rows":{}}""")]
        [InlineData("""{"rows":[1]}""")]
        [InlineData("""{"rows":[{"a":1}]}""")]
        [InlineData("""{"rows":[{"a":{"b":"c"}}]}""")]
        public void AnythingButObjectsOfStrings_IsUnreadable(string json)
        {
            Assert.ThrowsAny<JsonException>(() => Parse(json));
        }

        [Fact]
        public void EmptyRowsPastTheLimitInMemory_AreRefusedWhileRead()
        {
            // About a million empty rows: 3 MB of JSON, over 128 MB once read.
            var rows = (int)(SessionUploadOptions.MaxReadBytes / SessionDataStore.PerRowOverheadBytes) + 1;
            var json = new StringBuilder("""{"rows":[""");
            json.Insert(json.Length, "{},", rows - 1).Append("{}]}");

            Assert.ThrowsAny<JsonException>(() => Parse(json.ToString()));
        }

        [Fact]
        public void RowsJustUnderTheLimit_AreRead()
        {
            var rows = (int)(SessionUploadOptions.MaxReadBytes / SessionDataStore.PerRowOverheadBytes);
            var json = new StringBuilder("""{"rows":[""");
            json.Insert(json.Length, "{},", rows - 1).Append("{}]}");

            Assert.Equal(rows, Parse(json.ToString()).Rows.Count);
        }
    }
}
