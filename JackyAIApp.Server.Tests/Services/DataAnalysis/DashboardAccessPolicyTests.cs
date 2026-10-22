using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.DataAnalysis;

namespace JackyAIApp.Server.Tests.Services.DataAnalysis
{
    /// <summary>Who may see a dashboard, for every visibility and every kind of viewer.</summary>
    public class DashboardAccessPolicyTests
    {
        public enum Viewer { Owner, SharedWith, OtherSignedIn, Anonymous }

        private const string OwnerId = "owner";
        private const string SharedId = "shared";

        private static Dashboard DashboardWith(DashboardVisibility visibility) => new()
        {
            Id = Guid.NewGuid(),
            UserId = OwnerId,
            Name = "d",
            Visibility = visibility,
            // A share row even where the visibility ignores it: only Restricted reads it.
            Shares = [new DashboardShare { Id = Guid.NewGuid(), DashboardId = Guid.Empty, UserId = SharedId }],
        };

        private static string? IdOf(Viewer viewer) => viewer switch
        {
            Viewer.Owner => OwnerId,
            Viewer.SharedWith => SharedId,
            Viewer.OtherSignedIn => "someone-else",
            _ => null,
        };

        [Theory]
        [InlineData(DashboardVisibility.Private, Viewer.Owner, true)]
        [InlineData(DashboardVisibility.Private, Viewer.SharedWith, false)]
        [InlineData(DashboardVisibility.Private, Viewer.OtherSignedIn, false)]
        [InlineData(DashboardVisibility.Private, Viewer.Anonymous, false)]
        [InlineData(DashboardVisibility.Public, Viewer.Owner, true)]
        [InlineData(DashboardVisibility.Public, Viewer.SharedWith, true)]
        [InlineData(DashboardVisibility.Public, Viewer.OtherSignedIn, true)]
        [InlineData(DashboardVisibility.Public, Viewer.Anonymous, false)]
        [InlineData(DashboardVisibility.Restricted, Viewer.Owner, true)]
        [InlineData(DashboardVisibility.Restricted, Viewer.SharedWith, true)]
        [InlineData(DashboardVisibility.Restricted, Viewer.OtherSignedIn, false)]
        [InlineData(DashboardVisibility.Restricted, Viewer.Anonymous, false)]
        [InlineData(DashboardVisibility.Anyone, Viewer.Owner, true)]
        [InlineData(DashboardVisibility.Anyone, Viewer.SharedWith, true)]
        [InlineData(DashboardVisibility.Anyone, Viewer.OtherSignedIn, true)]
        [InlineData(DashboardVisibility.Anyone, Viewer.Anonymous, true)]
        public void CanView(DashboardVisibility visibility, Viewer viewer, bool expected)
        {
            Assert.Equal(expected, DashboardAccessPolicy.CanView(DashboardWith(visibility), IdOf(viewer)));
        }

        [Theory]
        [InlineData(DashboardVisibility.Private, Viewer.Owner, true)]
        [InlineData(DashboardVisibility.Private, Viewer.SharedWith, false)]
        [InlineData(DashboardVisibility.Public, Viewer.Owner, true)]
        [InlineData(DashboardVisibility.Public, Viewer.OtherSignedIn, false)] // reached by its link, not listed
        [InlineData(DashboardVisibility.Restricted, Viewer.SharedWith, true)]
        [InlineData(DashboardVisibility.Restricted, Viewer.OtherSignedIn, false)]
        [InlineData(DashboardVisibility.Anyone, Viewer.OtherSignedIn, false)]
        public void ListedFor(DashboardVisibility visibility, Viewer viewer, bool expected)
        {
            var listed = DashboardAccessPolicy.ListedFor(IdOf(viewer)!).Compile();

            Assert.Equal(expected, listed(DashboardWith(visibility)));
        }

        [Fact]
        public void EveryListedDashboard_IsOneTheViewerCanOpen()
        {
            foreach (var visibility in Enum.GetValues<DashboardVisibility>())
                foreach (var viewer in new[] { Viewer.Owner, Viewer.SharedWith, Viewer.OtherSignedIn })
                {
                    var dashboard = DashboardWith(visibility);
                    if (DashboardAccessPolicy.ListedFor(IdOf(viewer)!).Compile()(dashboard))
                        Assert.True(DashboardAccessPolicy.CanView(dashboard, IdOf(viewer)), $"{visibility}/{viewer}");
                }
        }

        [Theory]
        [InlineData("Private", true)]
        [InlineData("Public", true)]
        [InlineData("Restricted", true)]
        [InlineData("Anyone", true)]
        [InlineData("public", false)]
        [InlineData("1", false)]
        [InlineData(" Public", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void TryParseVisibility_TakesTheNamesAsWrittenOnly(string? value, bool accepted)
        {
            Assert.Equal(accepted, DashboardAccessPolicy.TryParseVisibility(value, out var visibility));
            if (accepted) Assert.Equal(value, visibility.ToString());
        }
    }
}
