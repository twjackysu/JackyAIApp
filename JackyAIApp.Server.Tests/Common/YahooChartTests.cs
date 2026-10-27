using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary><see cref="YahooChart.SymbolUrl"/>: a symbol stays in its own path segment, whatever a reader typed.</summary>
    public class YahooChartTests
    {
        [Theory]
        [InlineData("AAPL", "https://query1.finance.yahoo.com/v8/finance/chart/AAPL?range=1d")]
        [InlineData("2330.TW", "https://query1.finance.yahoo.com/v8/finance/chart/2330.TW?range=1d")]
        [InlineData("^GSPC", "https://query1.finance.yahoo.com/v8/finance/chart/%5EGSPC?range=1d")]
        [InlineData("../v7/x?a=1#b", "https://query1.finance.yahoo.com/v8/finance/chart/..%2Fv7%2Fx%3Fa%3D1%23b?range=1d")]
        public void SymbolUrl_EscapesTheSymbol(string symbol, string expected)
        {
            Assert.Equal(expected, YahooChart.SymbolUrl(symbol, "range=1d"));
        }

        [Fact]
        public void SymbolUrl_KeepsTheRequestOnYahoosChartPath()
        {
            var uri = new Uri(YahooChart.SymbolUrl("../../v7/finance/quote", "range=1d"));

            Assert.Equal("query1.finance.yahoo.com", uri.Host);
            Assert.StartsWith("/v8/finance/chart/", uri.AbsolutePath);
            Assert.Equal("?range=1d", uri.Query);
        }
    }
}
