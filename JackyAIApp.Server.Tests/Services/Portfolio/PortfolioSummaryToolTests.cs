using System.Text.Json;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Services.Portfolio.Assistant;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>Studio AI's read of the reader's own portfolio: the figures for the model, the positions as a card.</summary>
    public class PortfolioSummaryToolTests
    {
        private static readonly AiToolContext Context = new("u1", "c1");
        private static readonly JsonElement NoInput = JsonSerializer.SerializeToElement(new { });

        private static PortfolioSummaryDto Summary() => new()
        {
            EffectiveDate = new DateOnly(2026, 10, 23),
            UsdTwdRate = 32.1m,
            TotalNetWorthTWD = 1_234_567.4m,
            TwStockTWD = 1_000_000m,
            CashTWD = 234_567.4m,
            UnrealizedPnlTWD = -12_345.6m,
            UnrealizedPnlPct = -1.23m,
            DayChangeTWD = 5_000m,
            DayChangePct = 0.41m,
            Positions =
            [
                new PositionDto { AssetType = AssetType.CASH, Symbol = "TWD", Name = "現金", MarketValueTWD = 234_567.4m },
                new PositionDto
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電", MarketValueTWD = 1_000_000m,
                    UnrealizedPnlTWD = -12_345.6m, UnrealizedPnlPct = -1.22m, ExposureFactor = 1m,
                },
            ],
        };

        private static PortfolioSummaryTool Tool(PortfolioSummaryDto summary, string userId = "u1")
        {
            var portfolio = new Mock<IPortfolioService>();
            portfolio.Setup(p => p.GetPortfolioSummaryAsync(userId, It.IsAny<decimal>(), It.IsAny<CancellationToken>())).ReturnsAsync(summary);
            return new PortfolioSummaryTool(portfolio.Object);
        }

        [Fact]
        public async Task TheReadersPortfolio_IsDescribed_LargestPositionFirst()
        {
            var result = await Tool(Summary()).RunAsync(NoInput, Context, default);

            Assert.False(result.IsError);
            Assert.Contains("快照日期 2026-10-23", result.Content);
            Assert.Contains("淨資產 1,234,567", result.Content);
            Assert.Contains("未實現損益 -12,346（-1.23%", result.Content);
            Assert.Contains("今日變化 +5,000（+0.41%）", result.Content);
            Assert.True(result.Content.IndexOf("台積電", StringComparison.Ordinal) < result.Content.IndexOf("- 現金", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ThePositions_AreATableCard_TheClientCanDraw()
        {
            var result = await Tool(Summary()).RunAsync(NoInput, Context, default);

            var card = JsonSerializer.SerializeToElement(result.Card);
            Assert.Equal("table", card.GetProperty("kind").GetString());
            var rows = card.GetProperty("rows");
            Assert.Equal(2, rows.GetArrayLength());
            Assert.Equal("台積電（2330）", rows[0].GetProperty("name").GetString());
            Assert.Equal(4, card.GetProperty("columns").GetArrayLength());
            Assert.Equal(1_000_000m, rows[0].GetProperty("value").GetDecimal());
            Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("pnl").ValueKind);
            Assert.All(card.GetProperty("columns").EnumerateArray(), c => Assert.False(string.IsNullOrEmpty(c.GetProperty("key").GetString())));
        }

        [Fact]
        public async Task NoPositions_PointsTheReaderToThePortfolioPage()
        {
            var result = await Tool(new PortfolioSummaryDto()).RunAsync(NoInput, Context, default);

            Assert.Contains("還沒有", result.Content);
            Assert.Equal("/portfolio", JsonSerializer.SerializeToElement(result.Card).GetProperty("href").GetString());
        }

        [Fact]
        public async Task AFailure_IsReportedWithoutItsException()
        {
            var portfolio = new Mock<IPortfolioService>();
            portfolio.Setup(p => p.GetPortfolioSummaryAsync("u1", It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("db password=x"));

            var result = await new PortfolioSummaryTool(portfolio.Object).RunAsync(NoInput, Context, default);

            Assert.True(result.IsError);
            Assert.DoesNotContain("password", result.Content);
        }

        [Fact]
        public void ItIsARead_OfTheSitesOwn()
        {
            Assert.Equal((ToolEffect.Read, AiToolDescriptor.SiteConnector), (PortfolioSummaryTool.Definition.Effect, PortfolioSummaryTool.Definition.Connector));
        }
    }
}
