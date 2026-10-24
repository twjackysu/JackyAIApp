using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Moq;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>The connectors page's AI part: what the reader has, and what the AI may do with each tool.</summary>
    public class ConnectorCatalogServiceTests : IDisposable
    {
        private const string User = "u1";

        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero));

        public void Dispose() => _db.Dispose();

        private static AiToolDescriptor Tool(string name, ToolEffect effect) =>
            new(name, $"{name} 顯示名", $"{name} 說明", JsonSerializer.SerializeToElement(new { type = "object" }), effect);

        private ConnectorCatalogService Service()
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(User);
            var sets = new[]
            {
                new SiteToolSet("資料分析", [Tool("ask_data", ToolEffect.Read), Tool("save_widget", ToolEffect.Write)]),
                new SiteToolSet("單字", [Tool("clear_words", ToolEffect.Destructive)]),
            };
            return new ConnectorCatalogService(_db, user.Object, sets, _clock);
        }

        private static ToolPermissionRequestDto Request(string tool, string permission, string? installationId = null,
            string definitionKey = SiteConnector.Key) =>
            new() { DefinitionKey = definitionKey, InstallationId = installationId, ToolName = tool, Permission = permission };

        private SQL.ConnectorInstallation AddCustom(string userId = User, string? tools = null)
        {
            var row = new SQL.ConnectorInstallation
            {
                UserId = userId, DefinitionKey = SQL.ConnectorInstallation.CustomKey, DisplayName = "我的 MCP",
                ServerUrl = "https://mcp.example.com/mcp", AuthKind = "Bearer", LastStatus = "Ok", ToolsCacheJson = tools,
            };
            _db.ConnectorInstallations.Add(row);
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
            return row;
        }

        [Fact]
        public async Task TheSitesOwnTools_AreAlwaysThere_WithTheirDefaults()
        {
            var catalog = (await Service().GetAsync(default)).Data!;

            var site = Assert.Single(catalog.Installed);
            Assert.Null(site.Id);
            Assert.True(site.AlwaysOn);
            Assert.Equal("ok", site.Health);
            Assert.Equal(new[] { ("ask_data", "read", "auto"), ("save_widget", "write", "confirm"), ("clear_words", "destructive", "confirm") },
                site.Tools.Select(t => (t.Name, t.Effect, t.Permission)));
            // The catalog lists what can be added: the built-in MCP servers, not the site's own tools.
            Assert.Equal(BuiltInMcpConnectors.All.Select(d => d.Key), catalog.Entries.Select(e => e.Key));
            Assert.All(catalog.Entries, e => Assert.False(e.Installed));
            Assert.Equal(ConnectorCatalogService.CustomLimit, catalog.CustomLimit);
        }

        [Fact]
        public async Task TheFirstChangeToASiteTool_MakesARow_AndIsKept()
        {
            var changed = await Service().SetToolPermissionAsync(Request("ask_data", "disabled"), default);

            Assert.True(changed.IsSuccess);
            Assert.NotNull(changed.Data!.Id);
            Assert.Equal("disabled", changed.Data.Tools.Single(t => t.Name == "ask_data").Permission);
            var row = Assert.Single(_db.ConnectorInstallations.AsNoTracking());
            Assert.Equal(SiteConnector.Key, row.DefinitionKey);

            // A second change goes in the same row, by its id or without one.
            await Service().SetToolPermissionAsync(Request("save_widget", "auto", installationId: row.Id), default);
            await Service().SetToolPermissionAsync(Request("ask_data", "confirm"), default);
            var chosen = await Service().ChosenAsync(User, SiteConnector.Key, default);
            Assert.Equal(ToolPermission.Confirm, chosen["ask_data"]);
            Assert.Equal(ToolPermission.Auto, chosen["save_widget"]);
            Assert.Single(_db.ConnectorInstallations.AsNoTracking());
        }

        [Fact]
        public async Task AToolThatCannotBeUndone_CannotRunOnItsOwn()
        {
            var result = await Service().SetToolPermissionAsync(Request("clear_words", "auto"), default);

            Assert.Equal(ErrorCodes.BadRequest, result.Error);
            Assert.Empty(_db.ConnectorInstallations.AsNoTracking());
        }

        [Theory]
        [InlineData("nope", "auto")]
        [InlineData("ask_data", "always")]
        public async Task AnUnknownToolOrPermission_IsABadRequest(string tool, string permission)
        {
            var result = await Service().SetToolPermissionAsync(Request(tool, permission), default);

            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task AConnectorWithoutARow_OtherThanTheSites_IsNotFound()
        {
            var result = await Service().SetToolPermissionAsync(Request("ask_data", "auto", definitionKey: "github"), default);

            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task SomeoneElsesConnector_IsNotFound_ForEveryChange()
        {
            var theirs = AddCustom(userId: "u2", tools: """[{"name":"search","effect":"Read"}]""");

            Assert.Equal(ErrorCodes.NotFound, (await Service().SetToolPermissionAsync(
                Request("search", "disabled", theirs.Id, SQL.ConnectorInstallation.CustomKey), default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await Service().SetEnabledAsync(theirs.Id, false, default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await Service().DeleteAsync(theirs.Id, default)).Error);
            Assert.True(_db.ConnectorInstallations.AsNoTracking().Single().Enabled);
        }

        [Fact]
        public async Task ACustomServer_ShowsTheToolsItListed_AndCanBeTurnedOffAndRemoved()
        {
            var mine = AddCustom(tools: """[{"name":"search","title":"搜尋","description":"找東西","effect":"Read"},{"name":"create_issue"},{"description":"no name"}]""");

            var installed = (await Service().GetAsync(default)).Data!.Installed;
            var custom = installed.Single(c => c.Id == mine.Id);
            Assert.Equal("bearer", custom.AuthKind);
            // One without a name is left out; one without an effect counts as a write.
            Assert.Equal(new[] { ("search", "read", "auto"), ("create_issue", "write", "confirm") },
                custom.Tools.Select(t => (t.Name, t.Effect, t.Permission)));

            var off = await Service().SetEnabledAsync(mine.Id, false, default);
            Assert.Equal("disabled", off.Data!.Health);

            Assert.True((await Service().DeleteAsync(mine.Id, default)).IsSuccess);
            Assert.Empty(_db.ConnectorInstallations.AsNoTracking());
        }

        [Fact]
        public async Task TheSitesOwnConnector_CannotBeTurnedOffOrRemoved()
        {
            await Service().SetToolPermissionAsync(Request("ask_data", "disabled"), default);
            var row = _db.ConnectorInstallations.AsNoTracking().Single();

            Assert.Equal(ErrorCodes.NotFound, (await Service().SetEnabledAsync(row.Id, false, default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await Service().DeleteAsync(row.Id, default)).Error);
        }

        [Fact]
        public async Task AReaderWithNothingChosen_GetsNoChoices()
        {
            Assert.Empty(await Service().ChosenAsync(User, SiteConnector.Key, default));
        }
    }
}
