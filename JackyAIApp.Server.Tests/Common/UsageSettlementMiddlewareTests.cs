using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// The middleware settles every request's charges with the ledger's rule, so no endpoint has to
    /// refund by hand. Status 400 and up (and an exception) count as a failed request.
    /// </summary>
    public class UsageSettlementMiddlewareTests
    {
        private const string UserId = "settlement-test-user";

        private readonly Mock<IUsageLimitService> _limits = new();

        private DefaultHttpContext BuildContext()
        {
            var services = new ServiceCollection();
            services.AddSingleton(_limits.Object);
            return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        }

        private static UsageLedger ChargedLedger()
        {
            var ledger = new UsageLedger();
            ledger.RecordCharge(UserId, UsageFeature.ListeningTest, 4);
            return ledger;
        }

        private static UsageSettlementMiddleware Middleware(RequestDelegate next) =>
            new(next, NullLogger<UsageSettlementMiddleware>.Instance);

        private static RequestDelegate Respond(int status) => ctx =>
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        };

        [Theory]
        [InlineData(400)]
        [InlineData(404)]
        [InlineData(500)]
        [InlineData(502)]
        public async Task FailedResponse_NoAiBilled_Refunds(int status)
        {
            await Middleware(Respond(status)).InvokeAsync(BuildContext(), ChargedLedger());

            _limits.Verify(x => x.RefundAsync(UserId, UsageFeature.ListeningTest, 4), Times.Once);
        }

        [Fact]
        public async Task SuccessfulResponse_KeepsTheCharge()
        {
            await Middleware(Respond(200)).InvokeAsync(BuildContext(), ChargedLedger());

            _limits.Verify(x => x.RefundAsync(It.IsAny<string>(), It.IsAny<UsageFeature>(), It.IsAny<ulong>()), Times.Never);
        }

        [Fact]
        public async Task FailedResponse_AfterTheProviderBilled_KeepsTheCharge()
        {
            var ledger = ChargedLedger();
            ledger.MarkAiBilled();

            await Middleware(Respond(502)).InvokeAsync(BuildContext(), ledger);

            _limits.Verify(x => x.RefundAsync(It.IsAny<string>(), It.IsAny<UsageFeature>(), It.IsAny<ulong>()), Times.Never);
        }

        [Fact]
        public async Task UnhandledException_Refunds_AndStillPropagates()
        {
            var middleware = Middleware(_ => throw new InvalidOperationException("boom"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(BuildContext(), ChargedLedger()));

            _limits.Verify(x => x.RefundAsync(UserId, UsageFeature.ListeningTest, 4), Times.Once);
        }

        [Fact]
        public async Task RefundThatFails_DoesNotBreakTheResponse()
        {
            _limits.Setup(x => x.RefundAsync(It.IsAny<string>(), It.IsAny<UsageFeature>(), It.IsAny<ulong>()))
                .ThrowsAsync(new InvalidOperationException("db down"));

            await Middleware(Respond(500)).InvokeAsync(BuildContext(), ChargedLedger());
        }

        [Fact]
        public async Task NoCharge_NeverTouchesTheUsageService()
        {
            // No IUsageLimitService registered at all: resolving it would throw.
            var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };

            await Middleware(Respond(500)).InvokeAsync(context, new UsageLedger());
        }
    }
}
