using System.Security.Claims;
using System.Text.Json;
using DotnetSdkUtilities.Factory.ResponseFactory;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// <see cref="ICurrentUser"/> is the one way to ask who is signed in. Code behind [Authorize]
    /// uses <see cref="CurrentUserExtensions.RequireId"/> instead of writing a null check; if it
    /// ever runs without a user anyway, the request is answered as 401, not 500.
    /// </summary>
    public class CurrentUserTests
    {
        private static DefaultHttpContext SignedInAs(string id, string? name = null, string? email = null)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id) };
            if (name is not null) claims.Add(new Claim(ClaimTypes.Name, name));
            if (email is not null) claims.Add(new Claim(ClaimTypes.Email, email));
            return new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies")) };
        }

        [Fact]
        public void ReadsTheSignInClaims()
        {
            var user = TestCurrentUser.Of(SignedInAs("google-123", "Jacky", "j@example.com"));

            Assert.Equal("google-123", user.Id);
            Assert.Equal("Jacky", user.Name);
            Assert.Equal("j@example.com", user.Email);
            Assert.Equal("google-123", user.RequireId());
        }

        [Fact]
        public void Anonymous_HasNoId_AndRequireIdThrows()
        {
            Assert.Null(TestCurrentUser.Anonymous.Id);
            Assert.Throws<NotSignedInException>(() => TestCurrentUser.Anonymous.RequireId());
        }

        [Fact]
        public void NoRequestAtAll_HasNoId()
        {
            Assert.Null(new CurrentUser(new HttpContextAccessor()).Id);
        }

        [Fact]
        public async Task NotSignedIn_IsAnswered401_InTheErrorEnvelope()
        {
            var services = new ServiceCollection().AddLogging();
            services.AddMvcCore();
            services.AddSingleton<IMyResponseFactory>(new ResponseFactory(new ApiResponseFactory()));
            var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
            http.Response.Body = new MemoryStream();
            var middleware = new ExceptionMiddleware(
                _ => throw new NotSignedInException(), NullLogger<ExceptionMiddleware>.Instance);

            await middleware.InvokeAsync(http);

            Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
            http.Response.Body.Position = 0;
            using var body = await JsonDocument.ParseAsync(http.Response.Body);
            Assert.Equal("Not signed in.", body.RootElement.GetProperty("error").GetProperty("message").GetString());
        }
    }
}
