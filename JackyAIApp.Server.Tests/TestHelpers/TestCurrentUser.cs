using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Http;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>The real <see cref="ICurrentUser"/>, reading whoever <paramref name="http"/> is signed in as.</summary>
    public static class TestCurrentUser
    {
        public static ICurrentUser Of(HttpContext http) =>
            new CurrentUser(new HttpContextAccessor { HttpContext = http });

        /// <summary>An anonymous request.</summary>
        public static ICurrentUser Anonymous { get; } = Of(new DefaultHttpContext());
    }
}
