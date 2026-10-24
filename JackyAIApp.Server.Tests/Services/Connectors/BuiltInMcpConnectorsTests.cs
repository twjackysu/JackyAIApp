using System.Text.RegularExpressions;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>What every catalog MCP server must have; adding one to the catalog is checked here.</summary>
    public class BuiltInMcpConnectorsTests
    {
        public static TheoryData<string> Keys()
        {
            var keys = new TheoryData<string>();
            foreach (var d in BuiltInMcpConnectors.All) keys.Add(d.Key);
            return keys;
        }

        [Fact]
        public void Keys_AreUnique_AndNeitherTheSitesNorCustom()
        {
            var keys = BuiltInMcpConnectors.All.Select(d => d.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
            Assert.DoesNotContain(SiteConnector.Key, keys);
            Assert.DoesNotContain(JackyAIApp.Server.Data.Models.SQL.ConnectorInstallation.CustomKey, keys);
        }

        [Theory]
        [MemberData(nameof(Keys))]
        public void AnEntry_IsComplete_AndItsUrlIsOneWeTake(string key)
        {
            var d = BuiltInMcpConnectors.Find(key)!;

            Assert.Matches(new Regex("^[a-z][a-z0-9-]{1,40}$"), d.Key);
            Assert.False(string.IsNullOrWhiteSpace(d.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(d.Description));
            Assert.False(string.IsNullOrWhiteSpace(d.Publisher));
            Assert.Contains(d.Category, new[] { "data", "dev", "productivity", "finance" });
            Assert.Contains(d.AuthKind, new[] { "none", "oauth" });
            var (url, error) = McpServerUrl.Normalize(d.Url);
            Assert.Null(error);
            Assert.NotNull(url);
        }
    }
}
