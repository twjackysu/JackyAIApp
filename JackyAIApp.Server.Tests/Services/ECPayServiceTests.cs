using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// Covers the subscription (信用卡定期定額) flow added on top of ECPay's existing one-time
    /// AioCheckOut integration: first-charge callback (ReturnURL), period callback
    /// (PeriodReturnURL), and cancellation (CreditCardPeriodAction). The critical regression this
    /// guards is that TradeNo (constant across a subscription's whole lifetime) must never be used
    /// as the credit-grant idempotency key — only gwsr (unique per authorization) can distinguish
    /// period 1 from period 2 from period 3.
    /// </summary>
    public class ECPayServiceTests : IDisposable
    {
        private const string UserId = "user-1";
        private const string HashKey = "test-hash-key";
        private const string HashIV = "test-hash-iv";

        private readonly AzureSQLDBContext _dbContext;
        private readonly ECPayService _service;
        private readonly StubHttpMessageHandler _httpHandler;

        public ECPayServiceTests()
        {
            var dbOptions = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            _dbContext = new AzureSQLDBContext(dbOptions);
            _dbContext.Users.Add(new User
            {
                Id = UserId, Name = "U", Email = "u@example.com",
                CreditBalance = 0, TotalCreditsUsed = 0, LastUpdated = DateTime.UtcNow,
            });
            _dbContext.SaveChanges();

            var creditService = new CreditService(_dbContext, NullLogger<CreditService>.Instance);
            var ecpayOptions = Options.Create(new ECPayOptions
            {
                MerchantID = "2000132",
                HashKey = HashKey,
                HashIV = HashIV,
                BaseUrl = "https://payment-stage.ecpay.com.tw",
                AppBaseUrl = "https://jackyai.example.com",
            });
            _httpHandler = new StubHttpMessageHandler();
            var httpClient = new HttpClient(_httpHandler);

            _service = new ECPayService(
                creditService, _dbContext, ecpayOptions, httpClient, NullLogger<ECPayService>.Instance);
        }

        public void Dispose() => _dbContext.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Mirrors ECPayService's private GenerateCheckMacValue exactly, so tests can
        /// build a form ECPay's real callback would consider validly signed.</summary>
        private static string ComputeCheckMacValue(SortedDictionary<string, string> parameters)
        {
            var raw = string.Join("&", parameters.Select(kv => $"{kv.Key}={kv.Value}"));
            raw = $"HashKey={HashKey}&{raw}&HashIV={HashIV}";
            raw = HttpUtility.UrlEncode(raw).ToLower();

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return BitConverter.ToString(hash).Replace("-", "").ToUpper();
        }

        private static IFormCollection BuildForm(Dictionary<string, string> fields)
        {
            var sorted = new SortedDictionary<string, string>(fields);
            var withMac = new Dictionary<string, string>(fields)
            {
                ["CheckMacValue"] = ComputeCheckMacValue(sorted),
            };
            return new FormCollection(withMac.ToDictionary(kv => kv.Key, kv => new Microsoft.Extensions.Primitives.StringValues(kv.Value)));
        }

        private static Dictionary<string, string> SubscriptionCallbackFields(
            string tradeNo, string gwsr, string rtnCode = "1") => new()
        {
            ["MerchantID"] = "2000132",
            ["MerchantTradeNo"] = tradeNo,
            ["RtnCode"] = rtnCode,
            ["RtnMsg"] = rtnCode == "1" ? "Succeeded" : "Auth failed",
            ["CustomField1"] = UserId,
            ["CustomField2"] = SubscriptionPlans.ProId,
            ["gwsr"] = gwsr,
        };

        private class StubHttpMessageHandler : HttpMessageHandler
        {
            public string ResponseBody { get; set; } = "RtnCode=1&RtnMsg=Succeeded";
            public HttpRequestMessage? LastRequest { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ResponseBody),
                });
            }
        }

        // ── First-charge callback ────────────────────────────────────────────

        [Fact]
        public async Task HandleSubscriptionFirstChargeCallbackAsync_ValidCallback_CreatesActiveSubscriptionAndGrantsQuota()
        {
            var form = BuildForm(SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000001"));

            var ok = await _service.HandleSubscriptionFirstChargeCallbackAsync(form);

            Assert.True(ok);
            var subscription = await _dbContext.Subscriptions.FirstAsync(s => s.UserId == UserId);
            Assert.Equal(SubscriptionStatus.Active, subscription.Status);
            Assert.Equal("TRADE001", subscription.EcpayMerchantTradeNo);
            Assert.True(subscription.CurrentPeriodEnd > DateTime.UtcNow.AddDays(25));
            Assert.Equal(SubscriptionPlans.ProMonthlyQuota, (await _dbContext.Users.FirstAsync(u => u.Id == UserId)).CreditBalance);
        }

        [Fact]
        public async Task HandleSubscriptionFirstChargeCallbackAsync_TamperedMac_RejectsAndGrantsNothing()
        {
            var fields = SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000001");
            fields["CheckMacValue"] = "0000000000000000000000000000000000000000000000000000000000000000";
            var form = new FormCollection(fields.ToDictionary(kv => kv.Key, kv => new Microsoft.Extensions.Primitives.StringValues(kv.Value)));

            var ok = await _service.HandleSubscriptionFirstChargeCallbackAsync(form);

            Assert.False(ok);
            Assert.Empty(_dbContext.Subscriptions);
            Assert.Equal(0UL, (await _dbContext.Users.FirstAsync(u => u.Id == UserId)).CreditBalance);
        }

        [Fact]
        public async Task HandleSubscriptionFirstChargeCallbackAsync_RtnCodeNotOne_DoesNotGrant()
        {
            var form = BuildForm(SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000001", rtnCode: "10100058"));

            var ok = await _service.HandleSubscriptionFirstChargeCallbackAsync(form);

            Assert.False(ok);
            Assert.Equal(0UL, (await _dbContext.Users.FirstAsync(u => u.Id == UserId)).CreditBalance);
        }

        // ── Period callback ───────────────────────────────────────────────────

        [Fact]
        public async Task HandleSubscriptionPeriodCallbackAsync_SecondPeriod_GrantsFullQuotaAgardlessOfLeftoverBalance()
        {
            await _service.HandleSubscriptionFirstChargeCallbackAsync(
                BuildForm(SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000001")));
            var user = await _dbContext.Users.FirstAsync(u => u.Id == UserId);
            user.CreditBalance = 42; // simulate the user having mostly used up period 1's quota
            await _dbContext.SaveChangesAsync();

            var ok = await _service.HandleSubscriptionPeriodCallbackAsync(
                BuildForm(SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000002"))); // same TradeNo, new gwsr

            Assert.True(ok);
            Assert.Equal(SubscriptionPlans.ProMonthlyQuota, (await _dbContext.Users.FirstAsync(u => u.Id == UserId)).CreditBalance);
        }

        [Fact]
        public async Task HandleSubscriptionPeriodCallbackAsync_ReplayedGwsr_DoesNotRegrant()
        {
            await _service.HandleSubscriptionFirstChargeCallbackAsync(
                BuildForm(SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000001")));
            await _service.HandleSubscriptionPeriodCallbackAsync(
                BuildForm(SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000002")));
            var user = await _dbContext.Users.FirstAsync(u => u.Id == UserId);
            user.CreditBalance = 500; // user spent some of period 2's quota
            await _dbContext.SaveChangesAsync();

            // ECPay redelivers the exact same period-2 notification (same gwsr).
            var replay = await _service.HandleSubscriptionPeriodCallbackAsync(
                BuildForm(SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000002")));

            Assert.True(replay);
            // Regression guard: must stay at 500, not jump back to 3000 — this is exactly the bug
            // that would occur if TradeNo (constant across the whole subscription) were used as
            // the idempotency key instead of gwsr (unique per authorization).
            Assert.Equal(500UL, (await _dbContext.Users.FirstAsync(u => u.Id == UserId)).CreditBalance);
        }

        // ── Cancellation ──────────────────────────────────────────────────────

        [Fact]
        public async Task CancelSubscriptionAsync_EcpaySucceeds_MarksCancelledButKeepsCurrentPeriodEnd()
        {
            await _service.HandleSubscriptionFirstChargeCallbackAsync(
                BuildForm(SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000001")));
            var periodEndBeforeCancel = (await _dbContext.Subscriptions.FirstAsync(s => s.UserId == UserId)).CurrentPeriodEnd;

            var ok = await _service.CancelSubscriptionAsync(UserId);

            Assert.True(ok);
            var subscription = await _dbContext.Subscriptions.FirstAsync(s => s.UserId == UserId);
            Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
            Assert.NotNull(subscription.CancelledAt);
            // Paid-for access isn't revoked immediately — only future billing stops.
            Assert.Equal(periodEndBeforeCancel, subscription.CurrentPeriodEnd);
        }

        [Fact]
        public async Task CancelSubscriptionAsync_EcpayRejects_LeavesSubscriptionActive()
        {
            await _service.HandleSubscriptionFirstChargeCallbackAsync(
                BuildForm(SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000001")));
            _httpHandler.ResponseBody = "RtnCode=10200047&RtnMsg=Order+not+found";

            var ok = await _service.CancelSubscriptionAsync(UserId);

            Assert.False(ok);
            Assert.Equal(SubscriptionStatus.Active, (await _dbContext.Subscriptions.FirstAsync(s => s.UserId == UserId)).Status);
        }

        [Fact]
        public async Task CancelSubscriptionAsync_NoSubscriptionOnRecord_ReturnsFalseWithoutCallingEcpay()
        {
            var ok = await _service.CancelSubscriptionAsync(UserId);

            Assert.False(ok);
            Assert.Null(_httpHandler.LastRequest);
        }

        // ── Status ────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetSubscriptionStatusAsync_ActiveWithinPeriod_ReportsSubscribed()
        {
            await _service.HandleSubscriptionFirstChargeCallbackAsync(
                BuildForm(SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000001")));

            var status = await _service.GetSubscriptionStatusAsync(UserId);

            Assert.True(status.IsSubscribed);
            Assert.Equal(SubscriptionStatus.Active, status.Status);
            Assert.Equal(SubscriptionPlans.ProMonthlyQuota, status.CurrentBalance);
        }

        [Fact]
        public async Task GetSubscriptionStatusAsync_CancelledButStillWithinPaidPeriod_StillReportsSubscribed()
        {
            await _service.HandleSubscriptionFirstChargeCallbackAsync(
                BuildForm(SubscriptionCallbackFields(tradeNo: "TRADE001", gwsr: "1000001")));
            await _service.CancelSubscriptionAsync(UserId);

            var status = await _service.GetSubscriptionStatusAsync(UserId);

            Assert.True(status.IsSubscribed); // paid through CurrentPeriodEnd even though cancelled
            Assert.Equal(SubscriptionStatus.Cancelled, status.Status);
        }

        [Fact]
        public async Task GetSubscriptionStatusAsync_NeverSubscribed_ReportsNotSubscribed()
        {
            var status = await _service.GetSubscriptionStatusAsync(UserId);

            Assert.False(status.IsSubscribed);
            Assert.Null(status.Status);
        }
    }
}
