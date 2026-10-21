using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.AiUsage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// Every billable AI call is logged here, so this is where the request learns the provider billed
    /// — which is what keeps a failed request's usage charge from being refunded.
    /// </summary>
    public class AiUsageLoggerTests : IDisposable
    {
        private readonly AzureSQLDBContext _db = new(
            new DbContextOptionsBuilder<AzureSQLDBContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private readonly UsageLedger _ledger = new();

        public AiUsageLoggerTests() => _ledger.RecordCharge("logger-test-user", 2, "test_feature");

        public void Dispose() => _db.Dispose();

        private AiUsageLogger Logger() => new(_db, _ledger, NullLogger<AiUsageLogger>.Instance);

        /// <summary>A failed request still refunds unless something marked it billed.</summary>
        private bool Billed() => _ledger.TakeRefunds(responseFailed: true).Count == 0;

        [Fact]
        public async Task ChatThatUsedTokens_IsBilled_EvenWhenItFailed()
        {
            await Logger().LogChatAsync("f", "gpt-6-luna", promptTokens: 1200, completionTokens: 0, success: false, userId: null);

            Assert.True(Billed());
        }

        [Fact]
        public async Task ChatWithNoTokens_IsNotBilled()
        {
            await Logger().LogChatAsync("f", "claude-haiku-5-5", promptTokens: 0, completionTokens: 0, success: false, userId: null);

            Assert.False(Billed());
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public async Task TtsTranscriptionAndImage_AreBilledOnlyWhenTheySucceeded(bool success, bool billed)
        {
            await Logger().LogTtsAsync("tts-1", 20, success, null);
            await Logger().LogWhisperAsync("whisper-1", success, null);
            await Logger().LogImageGenerationAsync("f", "gpt-image-1", 1, success, null);

            Assert.Equal(billed, Billed());
        }
    }
}
