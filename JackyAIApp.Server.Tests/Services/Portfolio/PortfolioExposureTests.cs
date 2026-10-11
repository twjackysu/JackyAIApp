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

        [Fact]
        public void Factor_Liability_IsAlwaysZero()
        {
            // Debt itself isn't a market position — it just shrinks the net-worth denominator.
            var factor = PortfolioExposure.Factor(AssetType.LIABILITY, "信貸", "", "");
            Assert.Equal(0m, factor);
        }

        [Fact]
        public void Factor_RealEstate_IsAlwaysZero()
        {
            // A house is a real asset (counts toward net worth) but not a market position.
            var factor = PortfolioExposure.Factor(AssetType.REAL_ESTATE, "自住房", "", "");
            Assert.Equal(0m, factor);
        }

        [Theory]
        [InlineData("元大台灣50正2", "00631L", "TW", 2)]
        [InlineData("國泰台灣加權正二", "00663L", "TW", 2)]
        [InlineData("群益臺灣加權正2", "00685L", "TW", 2)]
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
        [InlineData("00685L", 2)] // 00685L 群益臺灣加權正2
        [InlineData("00757l", 2)] // lower-case suffix still matches (case-insensitive)
        [InlineData("00632R", -1)] // 反向 suffix
        [InlineData("00679B", 0)] // 債券ETF（被動）suffix
        [InlineData("00933D", 0)] // 債券ETF（主動）suffix
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

        [Theory]
        [InlineData("TQQQ", "ProShares UltraPro QQQ", 3)] // known-ticker table
        [InlineData("SQQQ", "ProShares UltraPro Short QQQ", -3)]
        [InlineData("QLD", "ProShares Ultra QQQ", 2)]
        public void Factor_UsKnownLeveragedTicker_ReturnsTableValue(string symbol, string name, int expected)
        {
            var factor = PortfolioExposure.Factor(AssetType.STOCK, name, symbol, "US");
            Assert.Equal(expected, factor);
        }

        [Theory]
        [InlineData("NVDL", "GraniteShares 2x Long NVDA Daily ETF", 2)] // explicit digit, not in known-ticker table
        [InlineData("SOXX2", "Direxion Daily Semiconductor Bull 3X Shares", 3)]
        [InlineData("SOXX3", "Direxion Daily Semiconductor Bear 3X Shares", -3)]
        [InlineData("XYZ1", "ProShares UltraShort S&P500", -2)]
        [InlineData("XYZ2", "ProShares Short S&P500", -1)] // plain inverse, no multiplier keyword
        public void Factor_UsLeveragedName_FallsBackToPatternMatch(string symbol, string name, int expected)
        {
            var factor = PortfolioExposure.Factor(AssetType.STOCK, name, symbol, "US");
            Assert.Equal(expected, factor);
        }

        [Fact]
        public void Factor_Ntsd_IsOnePointFiveTimesEquityExposure()
        {
            // 90% U.S. stock + 60% international equity futures. The name spells out no multiplier,
            // so without the table entry it would silently count as a plain 1x equity fund.
            var factor = PortfolioExposure.Factor(
                AssetType.STOCK, "WisdomTree Efficient U.S. Plus International Equity Fund", "NTSD", "US");

            Assert.Equal(1.5m, factor);
        }

        [Fact]
        public void Factor_Ntsd_MatchesRegardlessOfSymbolCase()
        {
            Assert.Equal(1.5m, PortfolioExposure.Factor(AssetType.STOCK, null, "ntsd", "US"));
        }

        [Fact]
        public void Factor_Ntsd_OnlyAppliesToTheUsListing()
        {
            // The table is US-only; a TW or fund symbol that happens to read NTSD must not pick it up.
            Assert.Equal(1m, PortfolioExposure.Factor(AssetType.FUND, "Some Fund", "NTSD", "FUND"));
        }

        [Fact]
        public void Factor_UsOrdinaryStock_DefaultsToOne()
        {
            var factor = PortfolioExposure.Factor(AssetType.STOCK, "Apple Inc.", "AAPL", "US");
            Assert.Equal(1m, factor);
        }
    }
}
