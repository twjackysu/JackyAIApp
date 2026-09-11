using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// Pins the normalization behaviour the two connector controllers used to each implement
    /// with their own private copy of a lower-cased switch.
    /// </summary>
    public class ConnectorProvidersTests
    {
        [Theory]
        [InlineData("Microsoft", "Microsoft")]
        [InlineData("microsoft", "Microsoft")]
        [InlineData("MICROSOFT", "Microsoft")]
        [InlineData("atlassian", "Atlassian")]
        [InlineData("AtLaSsIaN", "Atlassian")]
        [InlineData("google", "Google")]
        public void Normalize_ReturnsCanonicalCasing_ForAnyInputCasing(string input, string expected)
        {
            Assert.Equal(expected, ConnectorProviders.Normalize(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" google ")]
        [InlineData("dropbox")]
        public void Normalize_ReturnsNull_ForUnsupportedProvider(string? input)
        {
            Assert.Null(ConnectorProviders.Normalize(input));
        }

        [Fact]
        public void All_IsAlreadyInCanonicalForm()
        {
            Assert.All(ConnectorProviders.All, p => Assert.Equal(p, ConnectorProviders.Normalize(p)));
        }

        [Fact]
        public void SupportedNames_ListsEveryProvider()
        {
            Assert.Equal("Microsoft, Atlassian, Google", ConnectorProviders.SupportedNames);
        }
    }
}
