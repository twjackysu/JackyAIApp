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
