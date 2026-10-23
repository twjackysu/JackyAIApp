using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>Which rate converts which position: 集保 for funds, 國泰世華 on the quote's day for US stocks,
    /// mid-market for the rest in USD and as the fallback.</summary>
    public class PositionFxResolverTests
    {
        private const decimal Mid = 31.71m, Tdcc = 31.65m, Cathay = 31.68m;
        private static readonly DateOnly QuoteDay = new(2026, 10, 20);

        private static readonly Dictionary<string, MarketData> Stocks = new()
        {
            ["BRK-B"] = new MarketData
            {
                StockCode = "BRK-B",
                HistoricalPrices = [new DailyPrice { Date = QuoteDay.ToDateTime(TimeOnly.MinValue), Close = 508.13m }],
            },
        };

        private static readonly Dictionary<string, FundNavHistory> Funds = new()
        {
            ["FUNDUSD"] = new FundNavHistory("USD", [new PricePoint { Date = "2026-10-20", Close = 10.2m }]),
        };

        private static PositionFxResolver Resolver(
            Dictionary<string, decimal>? tdcc = null, Dictionary<(string, DateOnly), decimal>? cathay = null) =>
            new(Mid, tdcc ?? new() { ["USD"] = Tdcc }, cathay ?? new() { [("USD", QuoteDay)] = Cathay }, Stocks, Funds);

        private static HoldingPosition Position(AssetType type, string symbol, string currency) => new()
        {
            AssetType = type, Symbol = symbol, Market = type == AssetType.STOCK ? "US" : "", Currency = currency,
        };

        [Fact]
        public void Twd_IsOne_WithNoSource() =>
            Assert.Equal((1m, (string?)null), Resolver().Resolve(Position(AssetType.CASH, "", "TWD"), "TWD"));

        [Fact]
        public void AFund_ConvertsAtThe集保Rate() =>
            Assert.Equal((Tdcc, FxRateSources.Tdcc), Resolver().Resolve(Position(AssetType.FUND, "FUNDUSD", "USD"), "USD"));

        [Fact]
        public void AFund_WithNo集保Rate_FallsBackToMidMarket() =>
            Assert.Equal((Mid, FxRateSources.Mid),
                Resolver(tdcc: []).Resolve(Position(AssetType.FUND, "FUNDUSD", "USD"), "USD"));

        [Fact]
        public void AUsStock_ConvertsAtThe國泰世華RateOfItsQuotesDay() =>
            Assert.Equal((Cathay, FxRateSources.Cathay), Resolver().Resolve(Position(AssetType.STOCK, "BRK-B", "USD"), "USD"));

        [Fact]
        public void AUsStock_WithNoRateForItsQuotesDay_FallsBackToMidMarket()
        {
            var otherDay = new Dictionary<(string, DateOnly), decimal> { [("USD", QuoteDay.AddDays(1))] = 30m };

            Assert.Equal((Mid, FxRateSources.Mid),
                Resolver(cathay: otherDay).Resolve(Position(AssetType.STOCK, "BRK-B", "USD"), "USD"));
        }

        [Fact]
        public void UsdCash_ConvertsAtMidMarket_NotAPublishedRate() =>
            Assert.Equal((Mid, FxRateSources.Mid), Resolver().Resolve(Position(AssetType.CASH, "", "USD"), "USD"));

        [Fact]
        public void ACurrencyWithNoRate_IsOne_WithNoSource() =>
            Assert.Equal((1m, (string?)null), Resolver().Resolve(Position(AssetType.CASH, "", "JPY"), "JPY"));
    }
}
