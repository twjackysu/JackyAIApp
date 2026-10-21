using System.Text.Json;
using JackyAIApp.Server.DTO.DataAnalysis;

namespace JackyAIApp.Server.Tests.Services.DataAnalysis
{
    /// <summary>
    /// The session queries used to answer with anonymous objects (<c>new { fields }</c>, ...) that the
    /// client reads by name. They are named records now; serialized the way ASP.NET does (web
    /// defaults, camelCase), they must keep exactly the names the client reads.
    /// </summary>
    public class SessionResultShapeTests
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private static string[] KeysOf(object dto) =>
            [.. JsonDocument.Parse(JsonSerializer.Serialize(dto, Web)).RootElement.EnumerateObject().Select(p => p.Name)];

        public static TheoryData<object, string[]> Shapes => new()
        {
            { new SessionSchemaDto([]), ["fields"] },
            { new SessionMaxDateDto("2026-10-13T00:00:00.0000000"), ["maxDate"] },
            { new SessionDistinctValuesDto(["a"]), ["values"] },
            { new SessionRowsDto([]), ["rows"] },
            { new SessionDetailsDto([], 0), ["rows", "totalCount"] },
        };

        [Theory]
        [MemberData(nameof(Shapes))]
        public void KeepsTheNamesTheClientReads(object dto, string[] keys)
        {
            Assert.Equal(keys, KeysOf(dto));
        }

        [Fact]
        public void MaxDate_IsNullWhenTheColumnHasNoValue()
        {
            Assert.Equal("{\"maxDate\":null}", JsonSerializer.Serialize(new SessionMaxDateDto(null), Web));
        }
    }
}
