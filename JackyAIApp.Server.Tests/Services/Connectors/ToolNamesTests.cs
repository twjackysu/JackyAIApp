using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>
    /// A connector's tool names come from its server; the name the model sees must be one the API
    /// takes (<c>^[a-zA-Z0-9_-]{1,64}$</c>), name its connector, and stay unique.
    /// </summary>
    public class ToolNamesTests
    {
        [Fact]
        public void AToolIsNamedAfterItsConnector()
        {
            Assert.Equal("c3__search", ToolNames.ForModel("c3", "search"));
            Assert.Equal("site__dictionary_lookup", ToolNames.ForModel("site", "dictionary_lookup"));
        }

        [Fact]
        public void TwoConnectorsWithTheSameTool_GiveTwoNames()
        {
            Assert.NotEqual(ToolNames.ForModel("c3", "search"), ToolNames.ForModel("c4", "search"));
        }

        [Theory]
        [InlineData("search.docs", "c3__search_docs")]
        [InlineData("查詢", "c3____")]
        [InlineData("a b/c", "c3__a_b_c")]
        [InlineData("", "c3___")]
        public void CharactersTheApiRefuses_AreReplaced(string tool, string expected)
        {
            var name = ToolNames.ForModel("c3", tool);

            Assert.Equal(expected, name);
            Assert.True(ToolNames.IsValid(name));
        }

        [Fact]
        public void ATooLongName_IsCutToTheLimit_AndStaysUnique()
        {
            var first = ToolNames.ForModel("c3", new string('a', 80) + "_one");
            var second = ToolNames.ForModel("c3", new string('a', 80) + "_two");

            Assert.Equal(ToolNames.MaxLength, first.Length);
            Assert.True(ToolNames.IsValid(first));
            Assert.StartsWith("c3__aaaa", first);
            Assert.NotEqual(first, second);
            // The same tool always gets the same name.
            Assert.Equal(first, ToolNames.ForModel("c3", new string('a', 80) + "_one"));
        }

        [Fact]
        public void ANameAtTheLimit_IsKeptWhole()
        {
            var tool = new string('b', ToolNames.MaxLength - "c3__".Length);

            Assert.Equal("c3__" + tool, ToolNames.ForModel("c3", tool));
        }

        [Theory]
        [InlineData("", false)]
        [InlineData("ok_name-1", true)]
        [InlineData("has space", false)]
        [InlineData("點", false)]
        public void IsValid_FollowsTheApisRule(string name, bool valid)
        {
            Assert.Equal(valid, ToolNames.IsValid(name));
        }

        [Fact]
        public void IsValid_RefusesANameOverTheLimit()
        {
            Assert.False(ToolNames.IsValid(new string('x', ToolNames.MaxLength + 1)));
        }
    }
}
