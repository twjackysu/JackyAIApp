using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace JackyAIApp.Server.Tests.Common
{
    public class UsageLimitAttributeTests
    {
        private readonly Mock<ICurrentUser> _currentUserMock = new();
        private readonly Mock<IUsageLimitService> _usageLimitServiceMock = new();
        private readonly Mock<IMyResponseFactory> _responseFactoryMock = new();

        private const string TestUserId = "attribute-test-user";

        private ActionExecutingContext BuildContext()
        {
            var services = new ServiceCollection();
            services.AddSingleton(_currentUserMock.Object);
            services.AddSingleton(_usageLimitServiceMock.Object);
            services.AddSingleton(_responseFactoryMock.Object);

            var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            return new ActionExecutingContext(
                actionContext,
                new List<IFilterMetadata>(),
                new Dictionary<string, object?>(),
                controller: new object());
        }

        private static ActionExecutionDelegate NextDelegate(Action onNextCalled) => () =>
        {
            onNextCalled();
            return Task.FromResult<ActionExecutedContext>(null!);
        };

        [Fact]
        public async Task OnActionExecutionAsync_Unauthenticated_ShortCircuitsWithoutCallingNext()
        {
            _currentUserMock.Setup(x => x.Id).Returns((string?)null);
            var expectedResult = new StatusCodeResult(403);
            _responseFactoryMock
                .Setup(x => x.CreateErrorResponse(ErrorCodes.Unauthorized, It.IsAny<string>()))
                .Returns(expectedResult);
            var context = BuildContext();
            var nextCalled = false;
            var attribute = new UsageLimitAttribute(UsageFeature.StockAnalysis);

            await attribute.OnActionExecutionAsync(context, NextDelegate(() => nextCalled = true));

            Assert.False(nextCalled);
            Assert.Same(expectedResult, context.Result);
            _usageLimitServiceMock.Verify(
                x => x.TryConsumeAsync(It.IsAny<string>(), It.IsAny<UsageFeature>(), It.IsAny<ulong>()), Times.Never);
        }

        [Fact]
        public async Task OnActionExecutionAsync_WithinLimit_CallsNextWithoutSettingResult()
        {
            _currentUserMock.Setup(x => x.Id).Returns(TestUserId);
            _usageLimitServiceMock.Setup(x => x.TryConsumeAsync(TestUserId, UsageFeature.StockAnalysis, UsageCosts.Of(UsageFeature.StockAnalysis))).ReturnsAsync(true);
            var context = BuildContext();
            var nextCalled = false;
            var attribute = new UsageLimitAttribute(UsageFeature.StockAnalysis);

            await attribute.OnActionExecutionAsync(context, NextDelegate(() => nextCalled = true));

            Assert.True(nextCalled);
            Assert.Null(context.Result);
        }

        [Fact]
        public async Task OnActionExecutionAsync_ExceedsLimit_ShortCircuitsWithUsageLimitExceeded()
        {
            _currentUserMock.Setup(x => x.Id).Returns(TestUserId);
            _usageLimitServiceMock.Setup(x => x.TryConsumeAsync(TestUserId, UsageFeature.StockAnalysis, UsageCosts.Of(UsageFeature.StockAnalysis))).ReturnsAsync(false);
            var expectedResult = new StatusCodeResult(429);
            _responseFactoryMock
                .Setup(x => x.CreateErrorResponse(ErrorCodes.UsageLimitExceeded, It.IsAny<string>()))
                .Returns(expectedResult);
            var context = BuildContext();
            var nextCalled = false;
            var attribute = new UsageLimitAttribute(UsageFeature.StockAnalysis);

            await attribute.OnActionExecutionAsync(context, NextDelegate(() => nextCalled = true));

            Assert.False(nextCalled);
            Assert.Same(expectedResult, context.Result);
        }

        [Fact]
        public async Task OnActionExecutionAsync_ExceedsLimit_ErrorMessageNeverMentionsCreditsOrPayment()
        {
            _currentUserMock.Setup(x => x.Id).Returns(TestUserId);
            _usageLimitServiceMock.Setup(x => x.TryConsumeAsync(TestUserId, UsageFeature.StockAnalysis, UsageCosts.Of(UsageFeature.StockAnalysis))).ReturnsAsync(false);
            string? capturedMessage = null;
            _responseFactoryMock
                .Setup(x => x.CreateErrorResponse(ErrorCodes.UsageLimitExceeded, It.IsAny<string>()))
                .Callback<ErrorCodes, string>((_, message) => capturedMessage = message)
                .Returns(new StatusCodeResult(429));
            var context = BuildContext();
            var attribute = new UsageLimitAttribute(UsageFeature.StockAnalysis);

            await attribute.OnActionExecutionAsync(context, NextDelegate(() => { }));

            Assert.NotNull(capturedMessage);
            Assert.DoesNotContain("credit", capturedMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("purchase", capturedMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pay", capturedMessage, StringComparison.OrdinalIgnoreCase);
        }
    }
}
