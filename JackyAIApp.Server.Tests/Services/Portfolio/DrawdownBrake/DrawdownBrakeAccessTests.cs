using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    /// <summary>The Features:DrawdownBrake:Audience switch — closed to everyone but admins until the Boss opens it.</summary>
    public class DrawdownBrakeAccessTests
    {
        private readonly DbContextOptions<AzureSQLDBContext> _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        private DrawdownBrakeAccess CreateAccess(string? audience)
        {
            var settings = new Dictionary<string, string?>();
            if (audience != null) settings[DrawdownBrakeAccess.AudienceKey] = audience;
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            return new DrawdownBrakeAccess(config, new TestDbContextFactory(_options));
        }

        private async Task AddUserAsync(string id, bool? isAdmin)
        {
            await using var db = new AzureSQLDBContext(_options);
            db.Users.Add(new User { Id = id, LastUpdated = DateTime.UtcNow, IsAdmin = isAdmin });
            await db.SaveChangesAsync();
        }

        [Theory]
        [InlineData(null, true, true)]       // not configured → admins only
        [InlineData(null, false, false)]
        [InlineData(null, null, false)]
        [InlineData("Admin", true, true)]
        [InlineData("Admin", false, false)]
        [InlineData("All", false, true)]
        [InlineData("All", null, true)]
        [InlineData("all", false, true)]
        [InlineData(" ALL ", false, true)]
        [InlineData("Everyone", false, false)] // a typo must not open the feature up
        [InlineData("", false, false)]
        [InlineData("Everyone", true, true)]
        public void IsEnabledFor_Cases(string? audience, bool? isAdmin, bool expected) =>
            Assert.Equal(expected, CreateAccess(audience).IsEnabledFor(isAdmin));

        [Fact]
        public async Task IsEnabledForUserAsync_Admin_IsAllowed()
        {
            await AddUserAsync("boss", isAdmin: true);

            Assert.True(await CreateAccess(null).IsEnabledForUserAsync("boss"));
        }

        [Fact]
        public async Task IsEnabledForUserAsync_OrdinaryUser_IsRefusedWhileAudienceIsAdmin()
        {
            await AddUserAsync("someone", isAdmin: null);

            Assert.False(await CreateAccess("Admin").IsEnabledForUserAsync("someone"));
        }

        [Fact]
        public async Task IsEnabledForUserAsync_OrdinaryUser_IsAllowedOnceAudienceIsAll()
        {
            await AddUserAsync("someone", isAdmin: false);

            Assert.True(await CreateAccess("All").IsEnabledForUserAsync("someone"));
        }

        [Fact]
        public async Task IsEnabledForUserAsync_UnknownUser_IsRefusedWhileAudienceIsAdmin() =>
            Assert.False(await CreateAccess(null).IsEnabledForUserAsync("nobody"));
    }
}
