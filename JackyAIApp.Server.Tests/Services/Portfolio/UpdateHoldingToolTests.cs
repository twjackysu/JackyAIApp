using System.Text.Json;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Services.Portfolio.Assistant;
using JackyAIApp.Server.Tests.TestHelpers;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// Studio AI's change to one holding: the latest snapshot with that holding changed, saved as today's (Taipei),
    /// the cost following the average-cost rule, and nothing saved when the call is unclear.
    /// </summary>
    public class UpdateHoldingToolTests
    {
        private static readonly AiToolContext Context = new("u1", "c1");
        private static readonly DateOnly Today = new(2026, 10, 25);

        private static SaveHoldingsRequest Latest(DateOnly? date = null) => new()
        {
            EffectiveDate = date ?? new DateOnly(2026, 10, 20),
            Positions =
            [
                new PositionInput { AssetType = AssetType.STOCK, Symbol = "0050", Name = "元大台灣50", Market = "TW", Currency = "TWD", CostCurrency = "TWD", Quantity = 5000, CostBasis = 900_000m },
                new PositionInput { AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電", Market = "TW", Currency = "TWD", CostCurrency = "TWD", Quantity = 1000 },
                new PositionInput { AssetType = AssetType.CASH, Symbol = "", Name = "現金", Currency = "TWD", Quantity = 300_000m },
            ],
        };

        private sealed class Harness
        {
            public readonly Mock<IPortfolioService> Portfolio = new();
            public SaveHoldingsRequest? Saved;
            public readonly UpdateHoldingTool Tool;

            // 00:30 in Taipei is still the day before in UTC: the snapshot is the reader's today, not the server's.
            public Harness(SaveHoldingsRequest latest)
            {
                Portfolio.Setup(p => p.GetLatestHoldingsAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(latest);
                Portfolio.Setup(p => p.SaveHoldingsAsync("u1", It.IsAny<SaveHoldingsRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<string, SaveHoldingsRequest, CancellationToken>((_, req, _) => Saved = req)
                    .Returns(Task.CompletedTask);
                Tool = new UpdateHoldingTool(Portfolio.Object, new TestClock(TestClock.Taipei(Today, 0, 30)));
            }

            public Task<AiToolResult> Run(object input) => Tool.RunAsync(JsonSerializer.SerializeToElement(input), Context, default);
        }

        private static PositionInput Held(SaveHoldingsRequest req, string symbol) => Assert.Single(req.Positions, p => p.Symbol == symbol);

        [Fact]
        public async Task Buying_AddsTheShares_AndWhatWasPaid_SavedAsTodaysSnapshotInTaipei()
        {
            var h = new Harness(Latest());

            var result = await h.Run(new { symbol = "0050", change = 2000, price = 185 });

            Assert.False(result.IsError);
            Assert.NotNull(h.Saved);
            Assert.Equal(Today, h.Saved!.EffectiveDate);
            Assert.Equal(7000m, Held(h.Saved, "0050").Quantity);
            Assert.Equal(900_000m + 2000 * 185m, Held(h.Saved, "0050").CostBasis);
            // The other holdings are saved as they were.
            Assert.Equal(1000m, Held(h.Saved, "2330").Quantity);
            Assert.Equal(300_000m, Assert.Single(h.Saved.Positions, p => p.Name == "現金").Quantity);
            Assert.Contains("5,000 → 7,000", result.Content);
            Assert.Contains("2026-10-25", result.Content);
        }

        [Fact]
        public async Task Selling_KeepsTheAverageCost()
        {
            var h = new Harness(Latest());

            var result = await h.Run(new { symbol = "0050", change = -1000 });

            Assert.False(result.IsError);
            Assert.Equal(4000m, Held(h.Saved!, "0050").Quantity);
            Assert.Equal(720_000m, Held(h.Saved!, "0050").CostBasis);
        }

        [Fact]
        public async Task BuyingAHoldingWithACost_WithoutThePrice_SavesNothing_AndAsksForIt()
        {
            var h = new Harness(Latest());

            var result = await h.Run(new { symbol = "0050", quantity = 6000 });

            Assert.True(result.IsError);
            Assert.Contains("成交價", result.Content);
            Assert.Null(h.Saved);
        }

        [Fact]
        public async Task AHoldingWithoutACost_TakesTheNewQuantity_AndStaysWithoutOne()
        {
            var h = new Harness(Latest());

            await h.Run(new { symbol = "2330", quantity = 3000 });

            Assert.Equal(3000m, Held(h.Saved!, "2330").Quantity);
            Assert.Null(Held(h.Saved!, "2330").CostBasis);
        }

        [Fact]
        public async Task AHolding_IsFoundByItsName_AsWellAsItsSymbol()
        {
            var h = new Harness(Latest());

            await h.Run(new { symbol = "現金", change = 50_000 });

            Assert.Equal(350_000m, Assert.Single(h.Saved!.Positions, p => p.Name == "現金").Quantity);
        }

        [Fact]
        public async Task ZeroRemovesTheHolding_WhichTheSaveDropsAsTheEditorsEmptyRows()
        {
            var h = new Harness(Latest());

            var result = await h.Run(new { symbol = "2330", quantity = 0 });

            Assert.False(result.IsError);
            Assert.Equal(0m, Held(h.Saved!, "2330").Quantity);
            Assert.Contains("移除", result.Content);
        }

        [Fact]
        public async Task SellingMoreThanIsHeld_SavesNothing()
        {
            var h = new Harness(Latest());

            var result = await h.Run(new { symbol = "2330", change = -2000 });

            Assert.True(result.IsError);
            Assert.Null(h.Saved);
        }

        [Fact]
        public async Task AHoldingNotHeld_SavesNothing_AndPointsToThePortfolioPage()
        {
            var h = new Harness(Latest());

            var result = await h.Run(new { symbol = "AAPL", change = 10, price = 200 });

            Assert.True(result.IsError);
            Assert.Contains("0050", result.Content);
            Assert.NotNull(result.Card);
            Assert.Null(h.Saved);
        }

        [Fact]
        public async Task TwoHoldingsThatMatch_AreRefused_NotGuessed()
        {
            var latest = Latest();
            latest.Positions.Add(new PositionInput { AssetType = AssetType.CASH, Symbol = "", Name = "現金", Currency = "USD", Quantity = 1000 });
            var h = new Harness(latest);

            var result = await h.Run(new { symbol = "現金", change = 100 });

            Assert.True(result.IsError);
            Assert.Null(h.Saved);
        }

        [Fact]
        public async Task ALatestSnapshotDatedAhead_IsTheOneReplaced_SoTheChangeShows()
        {
            var ahead = Today.AddDays(3);
            var h = new Harness(Latest(ahead));

            await h.Run(new { symbol = "2330", quantity = 2000 });

            Assert.Equal(ahead, h.Saved!.EffectiveDate);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("""{"symbol":"0050"}""")]
        [InlineData("""{"symbol":"0050","quantity":100,"change":5}""")]
        [InlineData("""{"symbol":"0050","quantity":-1}""")]
        [InlineData("""{"symbol":"0050","change":0}""")]
        [InlineData("""{"symbol":"0050","change":5,"price":0}""")]
        [InlineData("""{"symbol":"","change":5}""")]
        public async Task AnUnclearCall_SavesNothing(string input)
        {
            var h = new Harness(Latest());

            var result = await h.Tool.RunAsync(JsonDocument.Parse(input).RootElement, Context, default);

            Assert.True(result.IsError);
            Assert.Null(h.Saved);
            Assert.Null(h.Tool.Describe(JsonDocument.Parse(input).RootElement, Context));
        }

        [Theory]
        [InlineData("""{"symbol":"0050","change":2000,"price":185}""", "「0050」加 2,000（成交價 185）")]
        [InlineData("""{"symbol":"0050","change":-1000}""", "「0050」減 1,000")]
        [InlineData("""{"symbol":"0050","quantity":3000}""", "把「0050」的數量改成 3,000")]
        [InlineData("""{"symbol":"0050","quantity":0}""", "把「0050」從你的投資組合移除")]
        public void TheConfirmationCard_SaysTheChangeInWords(string input, string expected)
        {
            var said = new Harness(Latest()).Tool.Describe(JsonDocument.Parse(input).RootElement, Context);

            Assert.Equal(expected + "（存成今天的快照）", said);
        }

        [Fact]
        public void ItIsASiteWrite_SoTheReaderIsAskedFirst()
        {
            Assert.Equal((ToolEffect.Write, AiToolDescriptor.SiteConnector), (UpdateHoldingTool.Definition.Effect, UpdateHoldingTool.Definition.Connector));
        }
    }
}
