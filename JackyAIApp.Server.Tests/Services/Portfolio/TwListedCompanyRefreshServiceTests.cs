using JackyAIApp.Server.Services.Portfolio;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    public class TwListedCompanyRefreshServiceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 6, 6, 0, 0, TimeSpan.Zero);

        private readonly Mock<ITwListedCompanyService> _companies = new();

        private TwListedCompanyRefreshService CreateService() =>
            new(_companies.Object, NullLogger<TwListedCompanyRefreshService>.Instance, new FakeTime(Now));

        [Fact]
        public async Task RefreshIfStaleAsync_RecentList_DoesNotRefresh()
        {
            _companies.Setup(c => c.GetLastRefreshedAtAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Now.UtcDateTime.AddHours(-11));

            await CreateService().RefreshIfStaleAsync(CancellationToken.None);

            _companies.Verify(c => c.RefreshAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(12)]
        [InlineData(24 * 7)]
        public async Task RefreshIfStaleAsync_OldList_Refreshes(int hoursOld)
        {
            _companies.Setup(c => c.GetLastRefreshedAtAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Now.UtcDateTime.AddHours(-hoursOld));

            await CreateService().RefreshIfStaleAsync(CancellationToken.None);

            _companies.Verify(c => c.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RefreshIfStaleAsync_EmptyList_Refreshes()
        {
            _companies.Setup(c => c.GetLastRefreshedAtAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateTime?)null);

            await CreateService().RefreshIfStaleAsync(CancellationToken.None);

            _companies.Verify(c => c.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RefreshIfStaleAsync_Throwing_DoesNotEndTheLoop()
        {
            // An exception escaping ExecuteAsync would stop the host, and with it the app.
            _companies.Setup(c => c.GetLastRefreshedAtAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("database unavailable"));

            await CreateService().RefreshIfStaleAsync(CancellationToken.None);
        }

        private sealed class FakeTime(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }
    }
}
