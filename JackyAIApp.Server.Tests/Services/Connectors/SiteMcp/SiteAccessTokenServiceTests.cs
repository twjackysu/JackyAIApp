using System.Security.Claims;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Connectors.SiteMcp;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Tests.Services.Connectors.SiteMcp
{
    /// <summary>The personal access tokens of the site's MCP server: made once, kept only as a hash, refused once revoked or expired.</summary>
    public class SiteAccessTokenServiceTests : IDisposable
    {
        private const string User = "u1";

        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));

        public void Dispose() => _db.Dispose();

        private SiteAccessTokenService Service(string? userId = User)
        {
            var http = new DefaultHttpContext();
            if (userId != null)
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
            return new SiteAccessTokenService(_db, TestCurrentUser.Of(http), _clock);
        }

        private static CreateSiteAccessTokenRequestDto Request(string name = "Claude Desktop", bool writes = false, int days = 90) =>
            new() { Name = name, AllowWrites = writes, ExpiresInDays = days };

        private async Task<string> CreateAsync(string userId = User, bool writes = false, int days = 90)
        {
            var made = await Service(userId).CreateAsync(Request(writes: writes, days: days), CancellationToken.None);
            Assert.True(made.IsSuccess);
            return made.Data!.Token;
        }

        [Fact]
        public async Task AToken_IsShownOnce_AndOnlyItsHashIsKept()
        {
            var made = await Service().CreateAsync(Request(name: "  Cursor  ", writes: true, days: 30), CancellationToken.None);

            var data = made.Data!;
            var token = data.Token;
            Assert.True(SiteAccessTokenFormat.IsWellFormed(token));
            Assert.StartsWith("jsk_", token);
            var row = Assert.Single(_db.SiteAccessTokens);
            Assert.Equal(SiteAccessTokenFormat.Hash(token), row.TokenHash);
            Assert.DoesNotContain(token, row.TokenHash);
            Assert.Equal("Cursor", row.Name);
            Assert.True(row.AllowWrites);
            Assert.Equal(_clock.Now.UtcDateTime.AddDays(30), row.ExpiresAt);
            Assert.Equal($"{token[..7]}…{token[^4..]}", data.Info.Hint);

            var listed = (await Service().ListAsync(CancellationToken.None)).Data!;
            Assert.Equal(data.Info, Assert.Single(listed.Tokens));
            Assert.Equal(SiteAccessTokenService.Limit, listed.Limit);
        }

        [Fact]
        public void EveryTokenIsNew()
        {
            var tokens = Enumerable.Range(0, 50).Select(_ => SiteAccessTokenFormat.Generate()).ToList();

            Assert.Equal(tokens.Count, tokens.Distinct().Count());
            Assert.All(tokens, t => Assert.Equal(SiteAccessTokenFormat.Length, t.Length));
        }

        [Theory]
        [InlineData("", 90)]
        [InlineData("   ", 90)]
        [InlineData("名字太長名字太長名字太長名字太長名字太長名字太長名字太長名字太長名字太長名字太長名字太長名字太長名字太長", 90)]
        [InlineData("ok", 7)]
        [InlineData("ok", 0)]
        public async Task ABadRequest_MakesNoToken(string name, int days)
        {
            var made = await Service().CreateAsync(Request(name: name, days: days), CancellationToken.None);

            Assert.Equal(ErrorCodes.BadRequest, made.Error);
            Assert.Empty(_db.SiteAccessTokens);
        }

        [Fact]
        public async Task AReader_MayHaveOnlySoMany()
        {
            for (var i = 0; i < SiteAccessTokenService.Limit; i++) await CreateAsync();

            var made = await Service().CreateAsync(Request(), CancellationToken.None);

            Assert.Equal(ErrorCodes.BadRequest, made.Error);
            Assert.True((await Service("u2").CreateAsync(Request(), CancellationToken.None)).IsSuccess);
        }

        [Fact]
        public async Task AGoodToken_StandsForItsOwner()
        {
            var token = await CreateAsync(writes: true);

            var holder = await Service(null).AuthenticateAsync(token, CancellationToken.None);

            Assert.NotNull(holder);
            Assert.Equal(User, holder.UserId);
            Assert.True(holder.AllowWrites);
            Assert.Equal(_db.SiteAccessTokens.Single().Id, holder.TokenId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("jsk_short")]
        [InlineData("not-a-token-of-ours-not-a-token-of-ours-xxxxx")]
        public async Task SomethingThatIsNotOurToken_IsRefused_WithoutALookup(string token)
        {
            Assert.Null(await Service(null).AuthenticateAsync(token, CancellationToken.None));
        }

        [Fact]
        public async Task AWellFormedToken_WeNeverMade_IsRefused()
        {
            await CreateAsync();

            Assert.Null(await Service(null).AuthenticateAsync(SiteAccessTokenFormat.Generate(), CancellationToken.None));
        }

        [Fact]
        public async Task AnExpiredToken_IsRefused()
        {
            var token = await CreateAsync(days: 30);
            _clock.Now = _clock.Now.AddDays(30);

            Assert.Null(await Service(null).AuthenticateAsync(token, CancellationToken.None));
        }

        [Fact]
        public async Task ARevokedToken_IsRefused_AndOnlyItsOwnerMayRevokeIt()
        {
            var token = await CreateAsync();
            var id = _db.SiteAccessTokens.Single().Id;

            Assert.Equal(ErrorCodes.NotFound, (await Service("u2").RevokeAsync(id, CancellationToken.None)).Error);
            Assert.NotNull(await Service(null).AuthenticateAsync(token, CancellationToken.None));

            Assert.True((await Service().RevokeAsync(id, CancellationToken.None)).IsSuccess);
            Assert.Null(await Service(null).AuthenticateAsync(token, CancellationToken.None));
            Assert.Empty((await Service().ListAsync(CancellationToken.None)).Data!.Tokens);
        }

        [Fact]
        public async Task Using_IsNoted_AtMostOnceAMinute()
        {
            var token = await CreateAsync();
            var start = _clock.Now.UtcDateTime;

            await Service(null).AuthenticateAsync(token, CancellationToken.None);
            Assert.Equal(start, _db.SiteAccessTokens.Single().LastUsedAt);

            _clock.Now = _clock.Now.AddSeconds(30);
            await Service(null).AuthenticateAsync(token, CancellationToken.None);
            Assert.Equal(start, _db.SiteAccessTokens.Single().LastUsedAt);

            _clock.Now = _clock.Now.AddSeconds(31);
            await Service(null).AuthenticateAsync(token, CancellationToken.None);
            Assert.Equal(_clock.Now.UtcDateTime, _db.SiteAccessTokens.Single().LastUsedAt);
        }
    }
}
