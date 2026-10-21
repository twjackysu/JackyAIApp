using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// The refund rule: a charge comes back only when the request failed and no AI provider billed
    /// for it. Everything else keeps the charge — a success (AI or not), and a failure the author
    /// already paid for.
    /// </summary>
    public class UsageLedgerTests
    {
        private static readonly UsageCharge Charge = new("ledger-test-user", UsageFeature.ClozeTest, 3);

        private static UsageLedger WithCharge()
        {
            var ledger = new UsageLedger();
            ledger.RecordCharge(Charge.UserId, Charge.Feature, Charge.Cost);
            return ledger;
        }

        [Fact]
        public void FailedWithoutAiBilling_RefundsTheCharge()
        {
            Assert.Equal([Charge], WithCharge().TakeRefunds(responseFailed: true));
        }

        [Fact]
        public void FailedAfterTheProviderBilled_KeepsTheCharge()
        {
            var ledger = WithCharge();
            ledger.MarkAiBilled();

            Assert.Empty(ledger.TakeRefunds(responseFailed: true));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Succeeded_KeepsTheCharge_WithOrWithoutAi(bool aiBilled)
        {
            var ledger = WithCharge();
            if (aiBilled) ledger.MarkAiBilled();

            Assert.Empty(ledger.TakeRefunds(responseFailed: false));
        }

        [Fact]
        public void MarkedFailed_CountsAsFailed_EvenWithASuccessStatus()
        {
            // A stream that had already sent 200 before it failed.
            var ledger = WithCharge();
            ledger.MarkFailed();

            Assert.Equal([Charge], ledger.TakeRefunds(responseFailed: false));
        }

        [Fact]
        public void TakeRefunds_AnswersOnce()
        {
            var ledger = WithCharge();

            Assert.Single(ledger.TakeRefunds(responseFailed: true));
            Assert.Empty(ledger.TakeRefunds(responseFailed: true));
        }

        [Fact]
        public void NoCharges_NothingToRefund()
        {
            Assert.Empty(new UsageLedger().TakeRefunds(responseFailed: true));
        }
    }
}
