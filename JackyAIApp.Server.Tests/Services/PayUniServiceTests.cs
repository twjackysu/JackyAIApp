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
using Microsoft.Extensions.Primitives;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// PayUni has no recurring-billing API (no PeriodAmount-style fields anywhere in its SDK),
    /// so unlike ECPay's subscription flow there is no first-charge/period-charge distinction —
    /// every payment is a fresh manual UPP checkout, verified identically by both NotifyURL
    /// (authoritative, server-to-server) and ReturnURL (browser redirect). The regression this
    /// guards is the same shape as ECPay's gwsr guard: PayUni's per-transaction TradeNo (not the
    /// merchant-chosen MerTradeNo, which callers could reuse) must be the credit-grant idempotency
    /// key, so a notify+return double-delivery for the same payment doesn't double-grant.
    /// </summary>
    public class PayUniServiceTests : IDisposable
    {
        private const string UserId = "user-1";
        private const string HashKey = "12345678901234567890123456789012"; // 32 bytes (AES-256)
        private const string HashIV = "1234567890123456"; // 16 bytes

        private readonly AzureSQLDBContext _dbContext;
        private readonly PayUniService _service;

        public PayUniServiceTests()
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
            var payuniOptions = Options.Create(new PayUniOptions
            {
                MerID = "U054186269",
                HashKey = HashKey,
                HashIV = HashIV,
                BaseUrl = "https://sandbox-api.payuni.com.tw/api/",
                AppBaseUrl = "https://jackyai.example.com",
            });

            _service = new PayUniService(creditService, _dbContext, payuniOptions, NullLogger<PayUniService>.Instance);
        }

        public void Dispose() => _dbContext.Dispose();

        // ── Crypto helpers — duplicate PayUniService's private Encrypt/Hash so tests can build
        // payloads a real PayUni callback would consider validly signed. Ported from PayUni's
        // official .NET SDK (github.com/payuni/NET_SDK), same algorithm PayUniService uses. ──────

        private static string Encrypt(string plaintext)
        {
            const int tagLengthBytes = 16;
            var key = Encoding.UTF8.GetBytes(HashKey);
            var iv = Encoding.UTF8.GetBytes(HashIV);
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            var cipher = new GcmBlockCipher(new AesEngine());
            var parameters = new AeadParameters(new KeyParameter(key), tagLengthBytes * 8, iv);
            cipher.Init(true, parameters);

            var outputBuffer = new byte[cipher.GetOutputSize(plaintextBytes.Length)];
            var offset = cipher.ProcessBytes(plaintextBytes, 0, plaintextBytes.Length, outputBuffer, 0);
            cipher.DoFinal(outputBuffer, offset);

            var cipherBytes = outputBuffer[..plaintextBytes.Length];
            var tagBytes = outputBuffer[plaintextBytes.Length..(plaintextBytes.Length + tagLengthBytes)];
            var combined = Convert.ToBase64String(cipherBytes) + ":::" + Convert.ToBase64String(tagBytes);
            return Convert.ToHexStringLower(Encoding.UTF8.GetBytes(combined));
        }

        private static string Decrypt(string encryptedHex)
        {
            var combined = Encoding.UTF8.GetString(Convert.FromHexString(encryptedHex));
            var parts = combined.Split([":::"], StringSplitOptions.RemoveEmptyEntries);
            var cipherBytes = Convert.FromBase64String(parts[0]);
            var tagBytes = Convert.FromBase64String(parts[1]);
            var key = Encoding.UTF8.GetBytes(HashKey);
            var iv = Encoding.UTF8.GetBytes(HashIV);

            var cipherAndTag = new byte[cipherBytes.Length + tagBytes.Length];
            Array.Copy(cipherBytes, cipherAndTag, cipherBytes.Length);
            Array.Copy(tagBytes, 0, cipherAndTag, cipherBytes.Length, tagBytes.Length);

            var cipher = new GcmBlockCipher(new AesEngine());
            var parameters = new AeadParameters(new KeyParameter(key), tagBytes.Length * 8, iv);
            cipher.Init(false, parameters);
            var outputBuffer = new byte[cipher.GetOutputSize(cipherAndTag.Length)];
            var offset = cipher.ProcessBytes(cipherAndTag, 0, cipherAndTag.Length, outputBuffer, 0);
            var len = offset + cipher.DoFinal(outputBuffer, offset);
            return Encoding.UTF8.GetString(outputBuffer, 0, len);
        }

        private static string Hash(string encryptInfo)
        {
            var raw = HashKey + encryptInfo + HashIV;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexStringLower(hash).ToUpperInvariant();
        }

        private static string BuildQueryString(IEnumerable<(string Key, string Value)> fields) =>
            string.Join("&", fields.Select(f => $"{f.Key}={HttpUtility.UrlEncode(f.Value)}"));

        /// <summary>Builds a validly-signed PayUni callback form (notify or return share the same
        /// shape) for the given decrypted-payload fields.</summary>
        private static IFormCollection BuildCallbackForm(Dictionary<string, string> plaintextFields)
        {
            var encryptInfo = Encrypt(BuildQueryString(plaintextFields.Select(kv => (kv.Key, kv.Value))));
            var hashInfo = Hash(encryptInfo);
            var form = new Dictionary<string, StringValues>
            {
                ["MerID"] = "U054186269",
                ["Version"] = "1.0",
                ["EncryptInfo"] = encryptInfo,
                ["HashInfo"] = hashInfo,
            };
            return new FormCollection(form);
        }

        private static Dictionary<string, string> PaidFields(string merTradeNo, string tradeNo, string tradeStatus = "1") => new()
        {
            ["MerTradeNo"] = merTradeNo,
            ["TradeNo"] = tradeNo,
            ["TradeAmt"] = SubscriptionPlans.ProPriceTWD.ToString(),
            ["TradeStatus"] = tradeStatus,
            ["Message"] = tradeStatus == "1" ? "授權成功" : "授權失敗",
        };

        // ── Crypto round-trip ─────────────────────────────────────────────────

        [Fact]
        public async Task CreateSubscriptionPaymentAsync_ProducesDecryptableFormTargetingUppEndpoint()
        {
            var form = await _service.CreateSubscriptionPaymentAsync(UserId);

            Assert.Equal("https://sandbox-api.payuni.com.tw/api/upp", form["PaymentGatewayUrl"]);
            Assert.Equal("U054186269", form["MerID"]);

            var decrypted = HttpUtility.ParseQueryString(Decrypt(form["EncryptInfo"]));
            Assert.Equal(SubscriptionPlans.ProPriceTWD.ToString(), decrypted["TradeAmt"]);
            Assert.Equal("https://jackyai.example.com/api/payuni/subscription/notify", decrypted["NotifyURL"]);
            Assert.Equal("https://jackyai.example.com/api/payuni/subscription/return", decrypted["ReturnURL"]);

            var subscription = await _dbContext.Subscriptions.FirstAsync(s => s.UserId == UserId);
            Assert.Equal(decrypted["MerTradeNo"], subscription.PayUniMerTradeNo);
            Assert.Equal("payuni", subscription.Provider);
        }

        // ── Notify callback ───────────────────────────────────────────────────

        [Fact]
        public async Task HandleNotifyCallbackAsync_ValidPaidCallback_ActivatesSubscriptionAndGrantsQuota()
        {
            var createForm = await _service.CreateSubscriptionPaymentAsync(UserId);
            var merTradeNo = HttpUtility.ParseQueryString(Decrypt(createForm["EncryptInfo"]))["MerTradeNo"]!;

            var ok = await _service.HandleNotifyCallbackAsync(
                BuildCallbackForm(PaidFields(merTradeNo, tradeNo: "PU-TRADE-001")));

            Assert.True(ok);
            var subscription = await _dbContext.Subscriptions.FirstAsync(s => s.UserId == UserId);
            Assert.Equal(SubscriptionStatus.Active, subscription.Status);
            Assert.True(subscription.CurrentPeriodEnd > DateTime.UtcNow.AddDays(25));
            Assert.Equal(SubscriptionPlans.ProMonthlyQuota, (await _dbContext.Users.FirstAsync(u => u.Id == UserId)).CreditBalance);
        }

        [Fact]
        public async Task HandleNotifyCallbackAsync_TamperedHashInfo_RejectsAndGrantsNothing()
        {
            var createForm = await _service.CreateSubscriptionPaymentAsync(UserId);
            var merTradeNo = HttpUtility.ParseQueryString(Decrypt(createForm["EncryptInfo"]))["MerTradeNo"]!;

            var encryptInfo = Encrypt(BuildQueryString(PaidFields(merTradeNo, "PU-TRADE-001").Select(kv => (kv.Key, kv.Value))));
            var form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["MerID"] = "U054186269",
                ["Version"] = "1.0",
                ["EncryptInfo"] = encryptInfo,
                ["HashInfo"] = "0000000000000000000000000000000000000000000000000000000000000000",
            });

            var ok = await _service.HandleNotifyCallbackAsync(form);

            Assert.False(ok);
            Assert.Equal(0UL, (await _dbContext.Users.FirstAsync(u => u.Id == UserId)).CreditBalance);
        }

        [Fact]
        public async Task HandleNotifyCallbackAsync_TradeStatusNotPaid_DoesNotGrant()
        {
            var createForm = await _service.CreateSubscriptionPaymentAsync(UserId);
            var merTradeNo = HttpUtility.ParseQueryString(Decrypt(createForm["EncryptInfo"]))["MerTradeNo"]!;

            var ok = await _service.HandleNotifyCallbackAsync(
                BuildCallbackForm(PaidFields(merTradeNo, tradeNo: "PU-TRADE-001", tradeStatus: "2"))); // 付款失敗

            Assert.False(ok);
            Assert.Equal(0UL, (await _dbContext.Users.FirstAsync(u => u.Id == UserId)).CreditBalance);
        }

        [Fact]
        public async Task HandleNotifyCallbackAsync_UnknownMerTradeNo_Rejects()
        {
            var ok = await _service.HandleNotifyCallbackAsync(
                BuildCallbackForm(PaidFields("no-such-order", tradeNo: "PU-TRADE-001")));

            Assert.False(ok);
            Assert.Empty(_dbContext.Subscriptions);
        }

        // ── Return callback + notify/return idempotency ──────────────────────

        [Fact]
        public async Task HandleReturnCallbackAsync_SameTradeNoAsNotify_DoesNotDoubleGrant()
        {
            var createForm = await _service.CreateSubscriptionPaymentAsync(UserId);
            var merTradeNo = HttpUtility.ParseQueryString(Decrypt(createForm["EncryptInfo"]))["MerTradeNo"]!;
            var paidFields = PaidFields(merTradeNo, tradeNo: "PU-TRADE-001");

            // NotifyURL (server-to-server) fires first, then ReturnURL (browser redirect) delivers
            // the exact same TradeNo — this is the expected normal-case double-delivery, not a bug.
            await _service.HandleNotifyCallbackAsync(BuildCallbackForm(paidFields));
            var user = await _dbContext.Users.FirstAsync(u => u.Id == UserId);
            user.CreditBalance = 500; // user already spent some of this period's quota
            await _dbContext.SaveChangesAsync();

            var returnOk = await _service.HandleReturnCallbackAsync(BuildCallbackForm(paidFields));

            Assert.True(returnOk);
            // Regression guard: must stay at 500, not jump back to 3000 — this is the PayUni
            // equivalent of ECPay's gwsr-vs-TradeNo bug, guarding against MerTradeNo (which a
            // caller could reuse) being mistaken for the idempotency key instead of PayUni's own
            // per-transaction TradeNo.
            Assert.Equal(500UL, (await _dbContext.Users.FirstAsync(u => u.Id == UserId)).CreditBalance);
        }

        // ── Cancellation ──────────────────────────────────────────────────────

        [Fact]
        public async Task CancelSubscriptionAsync_ExistingPayUniSubscription_MarksCancelledLocally()
        {
            var createForm = await _service.CreateSubscriptionPaymentAsync(UserId);
            var merTradeNo = HttpUtility.ParseQueryString(Decrypt(createForm["EncryptInfo"]))["MerTradeNo"]!;
            await _service.HandleNotifyCallbackAsync(BuildCallbackForm(PaidFields(merTradeNo, "PU-TRADE-001")));
            var periodEndBeforeCancel = (await _dbContext.Subscriptions.FirstAsync(s => s.UserId == UserId)).CurrentPeriodEnd;

            var ok = await _service.CancelSubscriptionAsync(UserId);

            Assert.True(ok);
            var subscription = await _dbContext.Subscriptions.FirstAsync(s => s.UserId == UserId);
            Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
            Assert.NotNull(subscription.CancelledAt);
            Assert.Equal(periodEndBeforeCancel, subscription.CurrentPeriodEnd); // paid-for access unaffected
        }

        [Fact]
        public async Task CancelSubscriptionAsync_NoSubscriptionOnRecord_ReturnsFalse()
        {
            var ok = await _service.CancelSubscriptionAsync(UserId);

            Assert.False(ok);
        }

        // ── Status ────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetSubscriptionStatusAsync_ActiveWithinPeriod_ReportsSubscribed()
        {
            var createForm = await _service.CreateSubscriptionPaymentAsync(UserId);
            var merTradeNo = HttpUtility.ParseQueryString(Decrypt(createForm["EncryptInfo"]))["MerTradeNo"]!;
            await _service.HandleNotifyCallbackAsync(BuildCallbackForm(PaidFields(merTradeNo, "PU-TRADE-001")));

            var status = await _service.GetSubscriptionStatusAsync(UserId);

            Assert.True(status.IsSubscribed);
            Assert.Equal(SubscriptionStatus.Active, status.Status);
            Assert.Equal(SubscriptionPlans.ProMonthlyQuota, status.CurrentBalance);
        }

        [Fact]
        public async Task GetSubscriptionStatusAsync_CancelledButStillWithinPaidPeriod_StillReportsSubscribed()
        {
            var createForm = await _service.CreateSubscriptionPaymentAsync(UserId);
            var merTradeNo = HttpUtility.ParseQueryString(Decrypt(createForm["EncryptInfo"]))["MerTradeNo"]!;
            await _service.HandleNotifyCallbackAsync(BuildCallbackForm(PaidFields(merTradeNo, "PU-TRADE-001")));
            await _service.CancelSubscriptionAsync(UserId);

            var status = await _service.GetSubscriptionStatusAsync(UserId);

            Assert.True(status.IsSubscribed);
            Assert.Equal(SubscriptionStatus.Cancelled, status.Status);
        }

        [Fact]
        public async Task GetSubscriptionStatusAsync_NeverSubscribed_ReportsNotSubscribed()
        {
            var status = await _service.GetSubscriptionStatusAsync(UserId);

            Assert.False(status.IsSubscribed);
            Assert.Null(status.Status);
        }

        [Fact]
        public async Task GetSubscriptionStatusAsync_ExpiredPastPeriodEnd_ReportsNotSubscribed()
        {
            // A PayUni subscriber who never renews just lapses naturally once CurrentPeriodEnd
            // passes — there's no auto-charge to fail and no cancel API to call.
            var createForm = await _service.CreateSubscriptionPaymentAsync(UserId);
            var merTradeNo = HttpUtility.ParseQueryString(Decrypt(createForm["EncryptInfo"]))["MerTradeNo"]!;
            await _service.HandleNotifyCallbackAsync(BuildCallbackForm(PaidFields(merTradeNo, "PU-TRADE-001")));
            var subscription = await _dbContext.Subscriptions.FirstAsync(s => s.UserId == UserId);
            subscription.CurrentPeriodEnd = DateTime.UtcNow.AddDays(-1);
            await _dbContext.SaveChangesAsync();

            var status = await _service.GetSubscriptionStatusAsync(UserId);

            Assert.False(status.IsSubscribed);
        }
    }
}
