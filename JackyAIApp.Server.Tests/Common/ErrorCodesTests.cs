using DotnetSdkUtilities.Factory.ResponseFactory;
using JackyAIApp.Server.Common;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>An error code's middle three digits are its HTTP status (<see cref="ResponseFactory"/>).</summary>
    public class ErrorCodesTests
    {
        private static int StatusOf(ErrorCodes code) =>
            ((ObjectResult)new ResponseFactory(Mock.Of<IApiResponseFactory>()).CreateErrorResponse(code)).StatusCode!.Value;

        [Fact]
        public void EveryCode_AnswersAnErrorStatus_ThatTheAppMeans()
        {
            // 405 is ASP.NET's own answer to a wrong HTTP verb; no failure of the app's means it.
            // ExternalApiError answered it by accident (1040500) until 2026-10.
            Assert.All(Enum.GetValues<ErrorCodes>(), code =>
            {
                var status = StatusOf(code);
                Assert.InRange(status, 400, 599);
                Assert.NotEqual(405, status);
            });
        }

        [Theory]
        [InlineData(ErrorCodes.ExternalApiError, 502)]
        [InlineData(ErrorCodes.NotFound, 404)]
        [InlineData(ErrorCodes.UsageLimitExceeded, 429)]
        public void KnownCodes_AnswerTheirStatus(ErrorCodes code, int status) => Assert.Equal(status, StatusOf(code));
    }
}
