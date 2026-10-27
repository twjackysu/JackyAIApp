using System.Reflection;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// A save of holdings is bounded (D-165): rows, the length of each text field, the request's size, and how many
    /// snapshots (dates) a reader keeps — beyond them a BadRequest and nothing saved.
    /// </summary>
    public class HoldingsLimitsTests
    {
        private static PositionInput Stock(string symbol = "2330", string name = "台積電") =>
            new() { AssetType = AssetType.STOCK, Symbol = symbol, Name = name, Market = "TW", Currency = "TWD", Quantity = 1000 };

        private static AzureSQLDBContext NewDb() => new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        [Fact]
        public void AnOrdinaryPortfolio_PassesUntouched()
        {
            Assert.Null(HoldingsLimits.Problem(new SaveHoldingsRequest { Positions = [Stock(), Stock("0050", "元大台灣50")] }));
            Assert.Null(HoldingsLimits.Problem(new SaveHoldingsRequest
            {
                Positions = [.. Enumerable.Range(0, HoldingsLimits.MaxPositions).Select(i => Stock($"S{i}", new string('名', HoldingsLimits.MaxNameLength)))],
            }));
        }

        [Fact]
        public void TooManyRows_OrAFieldTooLong_IsAProblem()
        {
            Assert.NotNull(HoldingsLimits.Problem(new SaveHoldingsRequest
            {
                Positions = [.. Enumerable.Range(0, HoldingsLimits.MaxPositions + 1).Select(i => Stock($"S{i}"))],
            }));
            Assert.NotNull(HoldingsLimits.Problem(new SaveHoldingsRequest { Positions = [Stock(name: new string('x', HoldingsLimits.MaxNameLength + 1))] }));
            Assert.NotNull(HoldingsLimits.Problem(new SaveHoldingsRequest { Positions = [Stock(symbol: new string('x', HoldingsLimits.MaxSymbolLength + 1))] }));
            var currency = Stock();
            currency.Currency = new string('x', HoldingsLimits.MaxCodeLength + 1);
            Assert.NotNull(HoldingsLimits.Problem(new SaveHoldingsRequest { Positions = [currency] }));
            var method = Stock();
            method.ValuationMethod = new string('x', HoldingsLimits.MaxCodeLength + 1);
            Assert.NotNull(HoldingsLimits.Problem(new SaveHoldingsRequest { Positions = [method] }));
        }

        [Fact]
        public async Task ASaveOverTheLimits_IsABadRequest_AndSavesNothing()
        {
            await using var db = NewDb();
            var service = new PortfolioServiceBuilder(db).Build();

            var result = await service.SaveHoldingsAsync("u1", new SaveHoldingsRequest
            {
                EffectiveDate = new DateOnly(2026, 10, 1),
                Positions = [Stock(name: new string('x', 30_000))],
            });

            Assert.Equal(ErrorCodes.BadRequest, result.Error);
            Assert.False(await db.HoldingSnapshots.AnyAsync());
        }

        [Fact]
        public async Task WithTheSnapshotsFull_ANewDateIsRefused_ButADateAlreadyKeptIsStillReplaced()
        {
            await using var db = NewDb();
            var first = new DateOnly(2020, 1, 1);
            db.HoldingSnapshots.AddRange(Enumerable.Range(0, HoldingsLimits.MaxSnapshots)
                .Select(i => new HoldingSnapshot { UserId = "u1", EffectiveDate = first.AddDays(i) }));
            // Someone else's snapshots are not counted against this reader.
            db.HoldingSnapshots.Add(new HoldingSnapshot { UserId = "u2", EffectiveDate = first });
            await db.SaveChangesAsync();
            var service = new PortfolioServiceBuilder(db).Build();

            var newDate = await service.SaveHoldingsAsync("u1", new SaveHoldingsRequest { EffectiveDate = new DateOnly(2026, 10, 1), Positions = [Stock()] });
            Assert.Equal(ErrorCodes.BadRequest, newDate.Error);
            Assert.Equal(HoldingsLimits.TooManySnapshotsMessage, newDate.ErrorMessage);
            Assert.Equal(HoldingsLimits.MaxSnapshots, await db.HoldingSnapshots.CountAsync(s => s.UserId == "u1"));

            var sameDate = await service.SaveHoldingsAsync("u1", new SaveHoldingsRequest { EffectiveDate = first, Positions = [Stock()] });
            Assert.True(sameDate.IsSuccess);
            Assert.Equal(HoldingsLimits.MaxSnapshots, await db.HoldingSnapshots.CountAsync(s => s.UserId == "u1"));

            var other = await service.SaveHoldingsAsync("u2", new SaveHoldingsRequest { EffectiveDate = new DateOnly(2026, 10, 1), Positions = [Stock()] });
            Assert.True(other.IsSuccess);
        }

        [Fact]
        public void TheSave_HoldsItsRequestBody_ToTheLimit()
        {
            var limit = typeof(PortfolioController).GetMethod(nameof(PortfolioController.SaveHoldings))!
                .GetCustomAttribute<RequestSizeLimitAttribute>();
            // Without it Kestrel reads up to its default (about 28 MB) before the save can refuse.
            Assert.NotNull(limit);
            Assert.Equal(512 * 1024, HoldingsLimits.MaxRequestBytes);
        }
    }
}
