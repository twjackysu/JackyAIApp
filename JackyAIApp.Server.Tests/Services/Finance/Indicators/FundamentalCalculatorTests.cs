using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Indicators;

namespace JackyAIApp.Server.Tests.Services.Finance.Indicators
{
    public class FundamentalCalculatorTests
    {
        #region PERatioCalculator Tests

        [Fact]
        public void PERatio_CanCalculate_ReturnsTrueWhenPresent()
        {
            var calc = new PERatioCalculator();
            var ctx = new IndicatorContext { Fundamentals = new FundamentalData { PERatio = 15 } };
            Assert.True(calc.CanCalculate(ctx));
        }

        [Fact]
        public void PERatio_CanCalculate_ReturnsFalseWhenNull()
        {
            var calc = new PERatioCalculator();
            Assert.False(calc.CanCalculate(new IndicatorContext()));
            Assert.False(calc.CanCalculate(new IndicatorContext { Fundamentals = new FundamentalData() }));
        }

        [Theory]
        [InlineData(8, SignalDirection.Bullish)]       // < 10: cheap
        [InlineData(12, SignalDirection.Bullish)]      // 10-15: reasonable low
        [InlineData(18, SignalDirection.Neutral)]      // 15-20: reasonable
        [InlineData(25, SignalDirection.Bearish)]      // 20-30: high
        [InlineData(40, SignalDirection.Bearish)]      // 30-50: high
        [InlineData(60, SignalDirection.StrongBearish)] // > 50: very high
        public void PERatio_Calculate_ReturnsCorrectDirection(decimal pe, SignalDirection expected)
        {
            var calc = new PERatioCalculator();
            var ctx = new IndicatorContext { Fundamentals = new FundamentalData { PERatio = pe } };
            var result = calc.Calculate(ctx);
            Assert.Equal(expected, result.Direction);
            Assert.Equal(pe, result.Value);
            Assert.Equal("PERatio", result.Name);
            Assert.Equal(IndicatorCategory.Fundamental, result.Category);
        }

        [Fact]
        public void PERatio_NegativePE_StrongBearish()
        {
            var calc = new PERatioCalculator();
            var ctx = new IndicatorContext { Fundamentals = new FundamentalData { PERatio = -5 } };
            var result = calc.Calculate(ctx);
            Assert.Equal(SignalDirection.StrongBearish, result.Direction);
            Assert.Equal(20, result.Score);
        }

        #endregion

        #region PBRatioCalculator Tests

        [Fact]
        public void PBRatio_CanCalculate_ReturnsTrueWhenPresent()
        {
            var calc = new PBRatioCalculator();
            var ctx = new IndicatorContext { Fundamentals = new FundamentalData { PBRatio = 1.2m } };
            Assert.True(calc.CanCalculate(ctx));
        }

        [Fact]
        public void PBRatio_CanCalculate_ReturnsFalseWhenNull()
        {
            var calc = new PBRatioCalculator();
            Assert.False(calc.CanCalculate(new IndicatorContext()));
        }

        [Theory]
        [InlineData(0.3, SignalDirection.Bullish)]    // < 0.5: very low
        [InlineData(0.8, SignalDirection.Bullish)]    // 0.5-1.0: below book
        [InlineData(1.2, SignalDirection.Neutral)]    // 1.0-1.5: reasonable
        [InlineData(2.5, SignalDirection.Neutral)]    // 1.5-3.0: moderate
        [InlineData(4.0, SignalDirection.Bearish)]    // 3.0-5.0: high
        [InlineData(6.0, SignalDirection.Bearish)]    // > 5.0: very high
        public void PBRatio_Calculate_ReturnsCorrectDirection(decimal pb, SignalDirection expected)
        {
            var calc = new PBRatioCalculator();
            var ctx = new IndicatorContext { Fundamentals = new FundamentalData { PBRatio = pb } };
            var result = calc.Calculate(ctx);
            Assert.Equal(expected, result.Direction);
            Assert.Equal(pb, result.Value);
        }

        #endregion

        #region DividendYieldCalculator Tests

        [Fact]
        public void DividendYield_CanCalculate_ReturnsTrueWhenPresent()
        {
            var calc = new DividendYieldCalculator();
            var ctx = new IndicatorContext { Fundamentals = new FundamentalData { DividendYield = 3.5m } };
            Assert.True(calc.CanCalculate(ctx));
        }

        [Theory]
        [InlineData(0, SignalDirection.Bearish)]      // <= 0: no dividend
        [InlineData(1.5, SignalDirection.Neutral)]    // < 2: low
        [InlineData(3, SignalDirection.Neutral)]      // 2-4: medium
        [InlineData(5, SignalDirection.Bullish)]      // 4-6: good
        [InlineData(7, SignalDirection.Bullish)]      // 6-8: high
        [InlineData(10, SignalDirection.Bullish)]     // > 8: very high
        public void DividendYield_Calculate_ReturnsCorrectDirection(decimal dy, SignalDirection expected)
        {
            var calc = new DividendYieldCalculator();
            var ctx = new IndicatorContext { Fundamentals = new FundamentalData { DividendYield = dy } };
            var result = calc.Calculate(ctx);
            Assert.Equal(expected, result.Direction);
        }

        #endregion

        #region RevenueGrowthCalculator Tests

        [Fact]
        public void RevenueGrowth_CanCalculate_ReturnsTrueWhenRevenuePresent()
        {
            var calc = new RevenueGrowthCalculator();
            var ctx = new IndicatorContext { Fundamentals = new FundamentalData { MonthlyRevenue = 1000000 } };
            Assert.True(calc.CanCalculate(ctx));
        }

        [Theory]
        [InlineData(25, 5, SignalDirection.StrongBullish)]   // YoY>20 && MoM>0
        [InlineData(15, -2, SignalDirection.Bullish)]        // YoY>10
        [InlineData(5, 3, SignalDirection.Bullish)]          // YoY>0 && MoM>0
        [InlineData(3, -1, SignalDirection.Neutral)]         // YoY>0
        [InlineData(-5, 0, SignalDirection.Neutral)]         // YoY>-10
        [InlineData(-15, -3, SignalDirection.Bearish)]       // YoY>-20
        [InlineData(-25, -10, SignalDirection.StrongBearish)] // YoY<=-20
        public void RevenueGrowth_Calculate_ReturnsCorrectDirection(decimal yoy, decimal mom, SignalDirection expected)
        {
            var calc = new RevenueGrowthCalculator();
            var ctx = new IndicatorContext
            {
                Fundamentals = new FundamentalData
                {
                    MonthlyRevenue = 5000000,
                    RevenueYoY = yoy,
                    RevenueMoM = mom
                }
            };
            var result = calc.Calculate(ctx);
            Assert.Equal(expected, result.Direction);
            Assert.Equal(yoy, result.Value); // Value = YoY
            Assert.Equal(3, result.SubValues.Count);
        }

        #endregion

        #region EPSCalculator Tests

        [Fact]
        public void EPS_CanCalculate_ReturnsTrueWhenPresent()
        {
            var calc = new EPSCalculator();
            var ctx = new IndicatorContext { Fundamentals = new FundamentalData { EPS = 3.5m } };
            Assert.True(calc.CanCalculate(ctx));
        }

        [Theory]
        [InlineData(-2, SignalDirection.StrongBearish)]  // < 0: loss
        [InlineData(0.3, SignalDirection.Bearish)]       // < 0.5: low
        [InlineData(0.8, SignalDirection.Neutral)]       // 0.5-1.0
        [InlineData(1.5, SignalDirection.Neutral)]       // 1.0-2.0
        [InlineData(3, SignalDirection.Bullish)]         // 2.0-5.0
        [InlineData(7, SignalDirection.Bullish)]         // 5.0-10.0
        [InlineData(15, SignalDirection.StrongBullish)]  // > 10
        public void EPS_Calculate_ReturnsCorrectDirection(decimal eps, SignalDirection expected)
        {
            var calc = new EPSCalculator();
            var ctx = new IndicatorContext
            {
                Fundamentals = new FundamentalData
                {
                    EPS = eps,
                    OperatingIncome = 100000,
                    NetIncome = 80000
                }
            };
            var result = calc.Calculate(ctx);
            Assert.Equal(expected, result.Direction);
            Assert.Equal(eps, result.Value);
        }

        [Fact]
        public void EPS_WithoutOptionalFields_StillWorks()
        {
            var calc = new EPSCalculator();
            var ctx = new IndicatorContext { Fundamentals = new FundamentalData { EPS = 5 } };
            var result = calc.Calculate(ctx);
            Assert.Single(result.SubValues); // Only EPS, no OperatingIncome/NetIncome
        }

        #endregion

        #region Category Consistency Tests

        [Fact]
        public void AllFundamentalCalculators_HaveCorrectCategory()
        {
            var calculators = new IIndicatorCalculator[]
            {
                new PERatioCalculator(),
                new PBRatioCalculator(),
                new DividendYieldCalculator(),
                new RevenueGrowthCalculator(),
                new EPSCalculator()
            };

            foreach (var calc in calculators)
            {
                Assert.Equal(IndicatorCategory.Fundamental, calc.Category);
            }
        }

        [Fact]
        public void AllFundamentalCalculators_HaveUniqueNames()
        {
            var calculators = new IIndicatorCalculator[]
            {
                new PERatioCalculator(),
                new PBRatioCalculator(),
                new DividendYieldCalculator(),
                new RevenueGrowthCalculator(),
                new EPSCalculator()
            };

            var names = calculators.Select(c => c.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }

        #endregion
    }
}
