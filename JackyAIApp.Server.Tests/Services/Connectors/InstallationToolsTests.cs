using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>A connector's name as kept (D-236): a server names itself, so what a reader cannot see is dropped.</summary>
    public class InstallationToolsTests
    {
        [Fact]
        public void ANameWithInvisibleCharacters_IsKeptWithoutThem()
        {
            // A direction override, a zero-width space and a Unicode tag character.
            var name = "Weather‮​" + char.ConvertFromUtf32(0xE0041) + " Pro";

            Assert.Equal("Weather Pro", InstallationTools.Trimmed(name, 100));
        }

        [Fact]
        public void LineBreaksAndTabs_BecomeSpaces()
        {
            Assert.Equal("a b c", InstallationTools.Trimmed(" a\nb\tc ", 100));
        }

        [Fact]
        public void ANameOfNothingVisible_IsNoName()
        {
            Assert.Null(InstallationTools.Trimmed("​‮", 100));
            Assert.Null(InstallationTools.Trimmed(null, 100));
        }

        [Fact]
        public void TheCut_NeverSplitsACharacter()
        {
            var name = "ab" + char.ConvertFromUtf32(0x1F600);

            Assert.Equal("ab", InstallationTools.Trimmed(name, 3));
            Assert.Equal("abc", InstallationTools.Trimmed("abcdef", 3));
        }
    }
}
