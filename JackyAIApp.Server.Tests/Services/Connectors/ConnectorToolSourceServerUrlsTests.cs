using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.Connectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>The addresses of the MCP servers a reader has added, for Studio AI's registry search (D-198).</summary>
    public class ConnectorToolSourceServerUrlsTests
    {
        private readonly DbContextOptions<AzureSQLDBContext> _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        private void Add(string userId, string? url, bool enabled = true)
        {
            using var db = new AzureSQLDBContext(_options);
            db.ConnectorInstallations.Add(new SQL.ConnectorInstallation
            {
                UserId = userId,
                DefinitionKey = SQL.ConnectorInstallation.CustomKey,
                ServerUrl = url,
                Enabled = enabled,
            });
            db.SaveChanges();
        }

        private async Task<IReadOnlySet<string>> UrlsOfAsync(string userId)
        {
            using var db = new AzureSQLDBContext(_options);
            return await new ConnectorToolSource(db, Mock.Of<IMcpToolClient>(), Mock.Of<IMcpOAuthTokens>(),
                NullLogger<ConnectorToolSource>.Instance).ServerUrlsAsync(userId, default);
        }

        [Fact]
        public async Task EveryServerTheReaderAdded_OnOrOff_InTheFormTheRegistrySearchComparesWith()
        {
            Add("u1", "https://mcp.deepwiki.com/mcp");
            Add("u1", "https://weather.example.com/mcp", enabled: false);
            Add("u1", null);
            Add("u2", "https://someone-elses.example.com/mcp");

            var urls = await UrlsOfAsync("u1");

            Assert.Equal(2, urls.Count);
            Assert.Contains(McpServerUrl.Key("https://MCP.deepwiki.com/mcp/")!, urls);
            Assert.Contains(McpServerUrl.Key("https://weather.example.com/mcp")!, urls);
        }

        [Fact]
        public async Task AReaderWithNone_HasNone()
        {
            Assert.Empty(await UrlsOfAsync("u1"));
        }
    }
}
