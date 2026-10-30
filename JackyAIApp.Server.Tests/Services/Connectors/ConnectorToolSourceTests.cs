using System.Text.Json;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.Connectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>Which of a reader's connectors' tools a chat turn is offered.</summary>
    public class ConnectorToolSourceTests
    {
        private readonly DbContextOptions<AzureSQLDBContext> _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        private void Add(string name, int tools, DateTime createdAt)
        {
            using var db = new AzureSQLDBContext(_options);
            db.ConnectorInstallations.Add(new SQL.ConnectorInstallation
            {
                UserId = "u1",
                DefinitionKey = SQL.ConnectorInstallation.CustomKey,
                DisplayName = name,
                ServerUrl = $"https://{name}.example.com/mcp",
                CreatedAt = createdAt,
                ToolsCacheJson = CachedTools.Serialize(Enumerable.Range(1, tools).Select(i => new CachedTool(
                    $"tool_{i}", null, "reads", ToolEffect.Read, JsonSerializer.SerializeToElement(new { type = "object" })))),
            });
            db.SaveChanges();
        }

        private async Task<IReadOnlyList<IAiTool>> ToolsAsync()
        {
            using var db = new AzureSQLDBContext(_options);
            return await new ConnectorToolSource(db, Mock.Of<IMcpToolClient>(), Mock.Of<IMcpOAuthTokens>(),
                NullLogger<ConnectorToolSource>.Instance).ForUserAsync("u1", default);
        }

        [Fact]
        public async Task ATurnIsOfferedAtMostMaxTools_TheConnectorsAddedFirstKeepingTheirs()
        {
            var day = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);
            Add("later", 40, day.AddHours(1));
            Add("first", 40, day);

            var tools = await ToolsAsync();

            Assert.Equal(ConnectorToolSource.MaxTools, tools.Count);
            Assert.Equal(40, tools.Count(t => t.Descriptor.ConnectorName == "first"));
            Assert.Equal(ConnectorToolSource.MaxTools - 40, tools.Count(t => t.Descriptor.ConnectorName == "later"));
        }

        [Fact]
        public async Task FewerTools_AreAllOffered()
        {
            Add("only", 12, new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(12, (await ToolsAsync()).Count);
        }
    }
}
