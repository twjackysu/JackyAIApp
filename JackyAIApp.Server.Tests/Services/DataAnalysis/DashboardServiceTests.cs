using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.DataAnalysis
{
    /// <summary>
    /// The dashboard endpoints' rules, now in <see cref="DashboardService"/>: what each user sees,
    /// that only the owner changes a dashboard or its sharing, and how sharing resolves emails.
    /// </summary>
    public class DashboardServiceTests : IDisposable
    {
        private const string Owner = "owner";
        private const string Friend = "friend";

        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        public DashboardServiceTests()
        {
            _db.Users.AddRange(
                new User { Id = Owner, Email = "owner@example.com", LastUpdated = DateTime.UtcNow },
                new User { Id = Friend, Email = "friend@example.com", Name = "Friend", LastUpdated = DateTime.UtcNow });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private DashboardService As(string? userId)
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(userId);
            return new DashboardServiceBuilder { Db = _db, Viewer = user.Object }.Build();
        }

        private async Task<Guid> CreateAsync(DashboardVisibility visibility = DashboardVisibility.Private, params string[] sharedWithEmails)
        {
            var created = await As(Owner).CreateAsync(new CreateDashboardRequest { Name = " Sales " }, default);
            var id = created.Data!.Id;
            if (visibility != DashboardVisibility.Private)
                await As(Owner).UpdateShareAsync(id, new UpdateShareRequest { Visibility = visibility.ToString(), Emails = [.. sharedWithEmails] }, default);
            return id;
        }

        [Fact]
        public async Task Create_TrimsTheName_AndRejectsAnEmptyOne()
        {
            var id = await CreateAsync();
            var empty = await As(Owner).CreateAsync(new CreateDashboardRequest { Name = "  " }, default);

            Assert.Equal("Sales", (await As(Owner).GetAsync(id, default)).Data!.Name);
            Assert.Equal(ErrorCodes.BadRequest, empty.Error);
        }

        [Fact]
        public async Task List_HasYourOwn_AndTheRestrictedOnesSharedWithYou_NotOthersPublicOnes()
        {
            var restricted = await CreateAsync(DashboardVisibility.Restricted, "friend@example.com");
            await CreateAsync(DashboardVisibility.Public);

            var ownerList = (await As(Owner).ListAsync(default)).Data!;
            var friendList = (await As(Friend).ListAsync(default)).Data!;

            Assert.Equal(2, ownerList.Count);
            Assert.All(ownerList, d => Assert.True(d.IsOwner));
            var shared = Assert.Single(friendList);
            Assert.Equal(restricted, shared.Id);
            Assert.False(shared.IsOwner);
        }

        [Theory]
        [InlineData(DashboardVisibility.Private, null, false)]
        [InlineData(DashboardVisibility.Private, Friend, false)]
        [InlineData(DashboardVisibility.Public, null, false)]
        [InlineData(DashboardVisibility.Public, Friend, true)]
        [InlineData(DashboardVisibility.Anyone, null, true)]
        public async Task Get_IsNotFound_ForAViewerItsVisibilityShutsOut(DashboardVisibility visibility, string? viewer, bool visible)
        {
            var id = await CreateAsync(visibility);

            var result = await As(viewer).GetAsync(id, default);

            Assert.Equal(visible, result.IsSuccess);
            if (visible) Assert.False(result.Data!.IsOwner);
            else Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task Sharing_ResolvesEmailsToUsers_LeavingOutTheOwnerAndUnknownOnes()
        {
            var id = await CreateAsync();

            var result = await As(Owner).UpdateShareAsync(id, new UpdateShareRequest
            {
                Visibility = "Restricted",
                Emails = [" friend@example.com ", "owner@example.com", "nobody@example.com", ""],
            }, default);

            Assert.Equal(DashboardVisibility.Restricted, result.Data!.Visibility);
            var member = Assert.Single(result.Data.Members);
            Assert.Equal(Friend, member.UserId);
            Assert.Equal("Friend", member.Name);
        }

        [Fact]
        public async Task LeavingRestricted_ClearsTheShares()
        {
            var id = await CreateAsync(DashboardVisibility.Restricted, "friend@example.com");

            var result = await As(Owner).UpdateShareAsync(id, new UpdateShareRequest { Visibility = "Public" }, default);

            Assert.Empty(result.Data!.Members);
            Assert.Empty(_db.DashboardShares);
        }

        [Fact]
        public async Task Sharing_TakesOnlyAKnownVisibilityName_AndOnlyFromTheOwner()
        {
            var id = await CreateAsync();

            var badName = await As(Owner).UpdateShareAsync(id, new UpdateShareRequest { Visibility = "public" }, default);
            var notOwner = await As(Friend).UpdateShareAsync(id, new UpdateShareRequest { Visibility = "Public" }, default);
            var notOwnerRead = await As(Friend).GetShareAsync(id, default);

            Assert.Equal(ErrorCodes.BadRequest, badName.Error);
            Assert.Equal("Invalid visibility.", badName.ErrorMessage);
            Assert.Equal(ErrorCodes.NotFound, notOwner.Error);
            Assert.Equal(ErrorCodes.NotFound, notOwnerRead.Error);
        }

        [Fact]
        public async Task OnlyTheOwner_RenamesDeletesOrRearranges_EvenOnAPublicDashboard()
        {
            var id = await CreateAsync(DashboardVisibility.Public);
            var friend = As(Friend);

            Assert.Equal(ErrorCodes.NotFound, (await friend.RenameAsync(id, new RenameDashboardRequest { Name = "x" }, default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await friend.DeleteAsync(id, default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await friend.UpdateLayoutAsync(id, new UpdateLayoutRequest { Items = [] }, default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await friend.AddWidgetAsync(id, Widget(), default)).Error);
            Assert.True((await As(Owner).DeleteAsync(id, default)).IsSuccess);
        }

        private static AddWidgetRequest Widget(int w = 6, int h = 8) => new()
        {
            VizType = "bar", Plan = JsonDocument.Parse("""{"viewBy":[],"measures":[]}""").RootElement, W = w, H = h,
        };

        [Fact]
        public async Task AddWidget_PlacesItBelowTheOthers_WithItsWidthClamped()
        {
            var id = await CreateAsync();

            var first = (await As(Owner).AddWidgetAsync(id, Widget(h: 5), default)).Data!;
            var second = (await As(Owner).AddWidgetAsync(id, Widget(w: 40), default)).Data!;

            Assert.Equal(0, first.Y);
            Assert.Equal(5, second.Y);
            Assert.Equal(12, second.W);
        }

        [Fact]
        public async Task RemoveWidget_OfSomeoneElsesDashboard_IsNotFound()
        {
            var id = await CreateAsync(DashboardVisibility.Public);
            var widget = (await As(Owner).AddWidgetAsync(id, Widget(), default)).Data!;

            Assert.Equal(ErrorCodes.NotFound, (await As(Friend).RemoveWidgetAsync(id, widget.Id, default)).Error);
            Assert.True((await As(Owner).RemoveWidgetAsync(id, widget.Id, default)).IsSuccess);
        }

        [Fact]
        public async Task WidgetData_IsGatedByTheDashboard_BeforeAnyQueryRuns()
        {
            // The resolver is a strict mock: none of these may reach it.
            var hidden = await CreateAsync();
            var open = await CreateAsync(DashboardVisibility.Anyone);
            _db.DashboardWidgets.Add(new DashboardWidget
            {
                Id = Guid.NewGuid(), DashboardId = open, VizType = "bar", PlanJson = "null", W = 6, H = 8,
            });
            await _db.SaveChangesAsync();
            var brokenWidget = _db.DashboardWidgets.Single().Id;

            Assert.Equal(ErrorCodes.NotFound, (await As(null).GetWidgetDataAsync(hidden, Guid.NewGuid(), default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await As(null).GetWidgetDataAsync(open, Guid.NewGuid(), default)).Error);
            var broken = await As(null).GetWidgetDataAsync(open, brokenWidget, default);
            Assert.Equal(ErrorCodes.InternalServerError, broken.Error);
            Assert.Equal("Invalid widget plan.", broken.ErrorMessage);
        }
    }
}
