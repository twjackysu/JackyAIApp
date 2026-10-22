using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// The pieces split out of PortfolioService: finding a held position in the latest snapshot (the
    /// price chart and the AI summary used to each match it themselves), and the AI summary built on it.
    /// </summary>
    public class HoldingServicesTests : IDisposable
    {
        private const string UserId = "holding-user";

        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        public void Dispose() => _db.Dispose();

        private async Task SeedAsync()
        {
            _db.HoldingSnapshots.AddRange(
                new HoldingSnapshot
                {
                    UserId = UserId,
                    EffectiveDate = new DateOnly(2026, 9, 1),
                    Positions = [new HoldingPosition { AssetType = AssetType.STOCK, Symbol = "2330", Market = "TW", Currency = "TWD", Quantity = 1 }],
                },
                new HoldingSnapshot
                {
                    UserId = UserId,
                    EffectiveDate = new DateOnly(2026, 10, 1),
                    Positions =
                    [
                        new HoldingPosition { AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電", Market = "TW", Currency = "TWD", Quantity = 2 },
                        new HoldingPosition { AssetType = AssetType.FUND, Symbol = "F1", Name = "某基金", Currency = "USD", Quantity = 10 },
                    ],
                },
                new HoldingSnapshot
                {
                    UserId = "someone-else",
                    EffectiveDate = new DateOnly(2026, 10, 2),
                    Positions = [new HoldingPosition { AssetType = AssetType.STOCK, Symbol = "2330", Market = "TW", Currency = "TWD", Quantity = 9 }],
                });
            await _db.SaveChangesAsync();
        }

        [Fact]
        public async Task FindLatestPosition_MatchesAStockOnItsMarket_AndAFundByIdAlone()
        {
            await SeedAsync();
            var reader = new HoldingSnapshotReader(_db);

            var stock = await reader.FindLatestPositionAsync(UserId, "tw", "2330", default);
            var fund = await reader.FindLatestPositionAsync(UserId, "FUND", "f1", default);

            Assert.Equal(2, stock!.Quantity); // the latest snapshot, not the older one or another user's
            Assert.Equal("某基金", fund!.Name);
            Assert.Null(await reader.FindLatestPositionAsync(UserId, "US", "2330", default));
            Assert.Null(await reader.FindLatestPositionAsync(UserId, "TW", "2317", default));
            Assert.Null(await reader.FindLatestPositionAsync("nobody", "TW", "2330", default));
        }

        [Fact]
        public async Task AiSummary_ForAPositionNotHeld_IsNotFound_AndAsksNoModel()
        {
            await SeedAsync();
            var openAI = new Mock<IOpenAIPromptService>();

            var result = await new PortfolioServiceBuilder(_db) { OpenAI = openAI.Object }
                .BuildAiSummary().GetAiTechSummaryAsync(UserId, "TW", "2317");

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
            openAI.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AiSummary_ForAHeldFund_SendsItsNavHistoryToTheZhuPrompt()
        {
            await SeedAsync();
            var fundData = new Mock<IFundDataService>();
            fundData.Setup(f => f.GetFundNavHistoryAsync("F1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundNavHistory("USD",
                    Enumerable.Range(1, 30).Select(i => new PricePoint { Date = $"2026-09-{i:00}", Close = 10 + i }).ToList()));
            var openAI = new Mock<IOpenAIPromptService>();
            string? userMessage = null;
            var answer = new PortfolioAiSummaryDto { TrendLabel = "多頭", Action = "續抱", Signals = "站上月線" };
            openAI.Setup(o => o.GetCompletionAsync<PortfolioAiSummaryDto>(
                    "Prompt/Portfolio/ZhuTechAnalysisSystem.txt", It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<IEnumerable<(string User, string Assistant)>?>(), It.IsAny<string?>()))
                .Callback<string, string, string?, IEnumerable<(string User, string Assistant)>?, string?>(
                    (_, user, _, _, _) => userMessage = user)
                .ReturnsAsync((answer, null));

            var result = await new PortfolioServiceBuilder(_db) { FundData = fundData.Object, OpenAI = openAI.Object }
                .BuildAiSummary().GetAiTechSummaryAsync(UserId, "FUND", "F1");

            Assert.True(result.IsSuccess);
            Assert.Same(answer, result.Data);
            Assert.Contains("F1 某基金", userMessage);
            Assert.Contains("最新收盤/淨值：40", userMessage);
        }
    }
}
