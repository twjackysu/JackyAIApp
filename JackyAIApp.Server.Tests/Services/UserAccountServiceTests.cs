using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// The signed-in user's own row, now behind <see cref="UserAccountService"/>: created on the
    /// first sign-in, and GET api/account/info telling the client whether to show 回落煞車
    /// (drawdownBrakeEnabled).
    /// </summary>
    public class UserAccountServiceTests : IDisposable
    {
        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        private readonly Mock<ICurrentUser> _currentUser = new();
        private readonly Mock<IDrawdownBrakeAccess> _access = new();
        private readonly UserAccountService _service;

        public UserAccountServiceTests()
        {
            _service = new UserAccountService(
                NullLogger<UserAccountService>.Instance, _db, _currentUser.Object, _access.Object);
        }

        public void Dispose() => _db.Dispose();

        private void SignInAs(string? id, string? name = null, string? email = null)
        {
            _currentUser.Setup(u => u.Id).Returns(id);
            _currentUser.Setup(u => u.Name).Returns(name);
            _currentUser.Setup(u => u.Email).Returns(email);
        }

        private async Task<AccountInfoResponse> InfoForAsync(bool? isAdmin, bool enabled)
        {
            _db.Users.Add(new User
            {
                Id = "u1", Name = "Jacky", Email = "jacky@example.com", LastUpdated = DateTime.UtcNow, IsAdmin = isAdmin,
                UsageLimitMultiplier = 3m, TotalXP = 120,
            });
            await _db.SaveChangesAsync();
            SignInAs("u1");
            _access.Setup(a => a.IsEnabledFor(isAdmin)).Returns(enabled);

            return (await _service.GetInfoAsync(default)).Data!;
        }

        [Fact]
        public async Task Info_FeatureOpenToTheUser_SaysEnabled() =>
            Assert.True((await InfoForAsync(isAdmin: true, enabled: true)).DrawdownBrakeEnabled);

        [Fact]
        public async Task Info_FeatureClosedToTheUser_SaysDisabled() =>
            Assert.False((await InfoForAsync(isAdmin: null, enabled: false)).DrawdownBrakeEnabled);

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(null, false)]
        public async Task Info_IsAdmin_IsFalseUnlessTheRowSaysSo(bool? stored, bool expected) =>
            Assert.Equal(expected, (await InfoForAsync(isAdmin: stored, enabled: false)).IsAdmin);

        [Fact]
        public async Task Info_CarriesOnlyWhatTheClientReads()
        {
            // Not the whole row: the usage-limit multiplier, XP, streaks and empty navigation
            // collections went out with it while the endpoint returned the entity.
            var json = System.Text.Json.JsonSerializer.SerializeToElement(
                await InfoForAsync(isAdmin: null, enabled: true), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

            Assert.Equal(["id", "name", "email", "isAdmin", "drawdownBrakeEnabled"], json.EnumerateObject().Select(p => p.Name));
            Assert.Equal(("u1", "Jacky", "jacky@example.com"),
                (json.GetProperty("id").GetString(), json.GetProperty("name").GetString(), json.GetProperty("email").GetString()));
        }

        [Fact]
        public async Task Info_WithoutTheUsersRow_AsksToSignInAgain()
        {
            SignInAs("ghost");

            var result = await _service.GetInfoAsync(default);

            Assert.Equal(UserAccountFailures.Missing.Error, result.Error);
            Assert.Equal(UserAccountFailures.Missing.Message, result.ErrorMessage);
        }

        [Fact]
        public async Task Exists_IsWhetherTheSignedInUsersRowIsThere()
        {
            _db.Users.Add(new User { Id = "u1", LastUpdated = DateTime.UtcNow });
            await _db.SaveChangesAsync();

            SignInAs("u1");
            Assert.True(await _service.ExistsAsync());
            SignInAs("ghost");
            Assert.False(await _service.ExistsAsync());
        }

        [Fact]
        public async Task TheFirstSignIn_CreatesTheRow_FromTheSignInClaims()
        {
            SignInAs("u1", "Jacky", "jacky@example.com");

            await _service.EnsureCreatedAsync(default);
            await _service.EnsureCreatedAsync(default);

            var user = Assert.Single(_db.Users);
            Assert.Equal(("u1", "Jacky", "jacky@example.com"), (user.Id, user.Name, user.Email));
        }

        [Fact]
        public async Task ALaterSignIn_LeavesTheRowAsItIs()
        {
            _db.Users.Add(new User { Id = "u1", Name = "Old name", LastUpdated = DateTime.UtcNow, TotalXP = 120 });
            await _db.SaveChangesAsync();
            SignInAs("u1", "New name", "new@example.com");

            await _service.EnsureCreatedAsync(default);

            var user = Assert.Single(_db.Users);
            Assert.Equal(("Old name", 120), (user.Name, user.TotalXP));
        }

        [Fact]
        public async Task AnAnonymousRequest_CreatesNothing()
        {
            SignInAs(null);

            await _service.EnsureCreatedAsync(default);

            Assert.Empty(_db.Users);
        }
    }
}
