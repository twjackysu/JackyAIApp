using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    public class PortfolioExposureTests
    {
        [Fact]
        public void Factor_Cash_IsAlwaysZero()
        {
            // Even if a cash row somehow carried a misleading name, cash exposure is always 0.
            var factor = PortfolioExposure.Factor(AssetType.CASH, "反2 test", "ANYTHING", "TW");
            Assert.Equal(0m, factor);
        }

        [Theory]
        [InlineData("元大台灣50正2", "00631L", "TW", 2)]
        [InlineData("國泰台灣加權正二", "00663L", "TW", 2)]
        [InlineData("槓桿黃金基金", "GOLDX", "FUND", 2)]
        [InlineData("元大台灣50反1", "00632R", "TW", -1)]
        [InlineData("國泰台灣加權反一", "00664R", "TW", -1)]
        [InlineData("反向石油ETF", "OILR", "FUND", -1)]
        [InlineData("元大S&P500反2", "SPXS2", "US", -2)]
        [InlineData("國泰台灣加權反二基金", "XYZ", "FUND", -2)]
        [InlineData("富達－全球優質債券基金", "ALI006", "FUND", 0)]
        [InlineData("元大美債20年", "00679B", "TW", 0)]
        [InlineData("貨幣市場基金", "MMF001", "FUND", 0)]
        [InlineData("台積電", "2330", "TW", 1)]
        [InlineData("Apple Inc.", "AAPL", "US", 1)]
        public void Factor_NameBasedClassification_TakesPriorityOverSuffix(
            string name, string symbol, string market, int expected)
        {
            // Name signals should win regardless of asset type or market, before any suffix
            // fallback is even considered.
            var factor = PortfolioExposure.Factor(AssetType.STOCK, name, symbol, market);
            Assert.Equal(expected, factor);
        }

        [Theory]
        [InlineData("00757L", 2)] // 槓桿 suffix
        [InlineData("00757l", 2)] // lower-case suffix still matches (case-insensitive)
        [InlineData("00632R", -1)] // 反向 suffix
        [InlineData("00679B", 0)] // 債券 suffix
        [InlineData("2330", 1)] // ordinary numeric code, no letter suffix
        public void Factor_TwStockWithGenericName_FallsBackToCodeSuffix(string symbol, int expected)
        {
            // Name carries no signal ("某ETF") so the TW-listed-stock suffix fallback applies.
            var factor = PortfolioExposure.Factor(AssetType.STOCK, "某ETF", symbol, "TW");
            Assert.Equal(expected, factor);
        }

        [Fact]
        public void Factor_UsStockEndingInL_DoesNotMisfireAsLeveraged()
        {
            // AAPL ends in "L" — the suffix fallback must be TW-market-only, or every Apple
            // holding in a US account would incorrectly be classified as a leveraged ETF.
            var factor = PortfolioExposure.Factor(AssetType.STOCK, "Apple Inc.", "AAPL", "US");
            Assert.Equal(1m, factor);
        }

        [Fact]
        public void Factor_FundWithTwCodeEndingInL_DoesNotUseSuffixFallback()
        {
            // Suffix fallback only applies to AssetType.STOCK — a fund's code shape is
            // unrelated to TW ETF ticker conventions and shouldn't be guessed from it.
            var factor = PortfolioExposure.Factor(AssetType.FUND, "某基金", "ABCL", "TW");
            Assert.Equal(1m, factor);
        }

        [Fact]
        public void Factor_NoSignalAtAll_DefaultsToOne()
        {
            var factor = PortfolioExposure.Factor(AssetType.STOCK, "台積電", "2330", "TW");
            Assert.Equal(1m, factor);
        }

        [Fact]
        public void Factor_NullNameAndSymbol_DoesNotThrow()
        {
            var factor = PortfolioExposure.Factor(AssetType.STOCK, null, null, "TW");
            Assert.Equal(1m, factor);
        }
    }
}
