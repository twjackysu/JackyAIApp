using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    public class ToolPermissionsTests
    {
        [Theory]
        [InlineData(ToolEffect.Read, ToolPermission.Auto)]
        [InlineData(ToolEffect.Write, ToolPermission.Confirm)]
        [InlineData(ToolEffect.Destructive, ToolPermission.Confirm)]
        public void ByDefault_OnlyAReadRunsOnItsOwn(ToolEffect effect, ToolPermission expected)
        {
            Assert.Equal(expected, ToolPermissions.For("t", effect, new Dictionary<string, ToolPermission>()));
        }

        [Fact]
        public void TheReadersChoice_Wins_ExceptAutoForWhatCannotBeUndone()
        {
            var chosen = ToolPermissions.Parse("""{"a":"Disabled","b":"Auto","c":"Auto"}""");

            Assert.Equal(ToolPermission.Disabled, ToolPermissions.For("a", ToolEffect.Read, chosen));
            Assert.Equal(ToolPermission.Auto, ToolPermissions.For("b", ToolEffect.Write, chosen));
            Assert.Equal(ToolPermission.Confirm, ToolPermissions.For("c", ToolEffect.Destructive, chosen));
        }

        [Fact]
        public void AToolThatAlwaysAsks_NeverRunsOnItsOwn_WhateverWasStored()
        {
            var schema = System.Text.Json.JsonSerializer.SerializeToElement(new { type = "object" });
            var remember = new AiToolDescriptor("remember", "記住", "keeps a note about the reader", schema, ToolEffect.Write, AlwaysAsks: true);
            var plain = remember with { Name = "plain", AlwaysAsks = false };
            var chosen = ToolPermissions.Parse("""{"remember":"Auto","plain":"Auto"}""");

            Assert.Equal(ToolPermission.Confirm, ToolPermissions.For(remember, chosen));
            Assert.Equal(ToolPermission.Auto, ToolPermissions.For(plain, chosen));
            Assert.False(ToolPermissions.Allowed(remember, ToolPermission.Auto));
            Assert.True(ToolPermissions.Allowed(remember, ToolPermission.Disabled));
            Assert.Equal(ToolPermission.Disabled,
                ToolPermissions.For(remember, ToolPermissions.Parse("""{"remember":"Disabled"}""")));
            // The site's MCP server never offers it, even to a token that may write.
            Assert.False(JackyAIApp.Server.Services.Connectors.SiteMcp.SiteMcpServer.Offered(remember, chosen, allowWrites: true));
            Assert.True(JackyAIApp.Server.Services.Connectors.SiteMcp.SiteMcpServer.Offered(plain, chosen, allowWrites: true));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        public void UnreadableChoices_AreNoChoices(string? json)
        {
            Assert.Empty(ToolPermissions.Parse(json));
        }

        [Fact]
        public void AStoredValueThatIsNotAPermission_IsLeftOut()
        {
            var chosen = ToolPermissions.Parse("""{"a":"Sometimes","b":"Confirm","c":"7"}""");

            Assert.Equal(new[] { "b" }, chosen.Keys);
        }

        [Fact]
        public void Choices_RoundTrip()
        {
            var chosen = new Dictionary<string, ToolPermission> { ["z"] = ToolPermission.Auto, ["a"] = ToolPermission.Disabled };

            Assert.Equal("""{"a":"Disabled","z":"Auto"}""", ToolPermissions.Serialize(chosen));
            Assert.Equal(chosen, ToolPermissions.Parse(ToolPermissions.Serialize(chosen)));
        }

        [Theory]
        [InlineData("auto", ToolPermission.Auto)]
        [InlineData("confirm", ToolPermission.Confirm)]
        [InlineData("disabled", ToolPermission.Disabled)]
        public void WireNames_RoundTrip(string name, ToolPermission permission)
        {
            Assert.Equal(name, permission.WireName());
            Assert.Equal(permission, ToolPermissions.FromWireName(name));
        }

        [Fact]
        public void AnUnknownWireName_IsNone()
        {
            Assert.Null(ToolPermissions.FromWireName("Auto"));
        }
    }
}
