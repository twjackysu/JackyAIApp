using System.Security.Cryptography;
using System.Text;
using System.Web;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// PayUni (統一金流) integration for the Pro subscription. Ported against PayUni's official
    /// .NET SDK (github.com/payuni/NET_SDK) and the PAYUNi_for_WooCommerce reference plugin —
    /// verified 2026-07-21: AES-256-GCM field encryption with a 16-byte IV (BouncyCastle required;
    /// .NET's built-in AesGcm hard-restricts nonces to 12 bytes), SHA-256 hash of
    /// HashKey+EncryptInfo+HashIV for request signing/response verification, and TradeStatus=="1"
    /// (已付款) in the decrypted payload as the payment-success signal.
    ///
    /// Unlike ECPay, PayUni has no periodic/recurring-billing API in this SDK (no PeriodAmount-
    /// style fields anywhere in EncryptInfoModel) — only one-time UPP checkout plus a separate
    /// card-token bind/query/cancel set of endpoints intended for merchant-triggered repeat
    /// charges. Per product decision, subscriptions are NOT auto-renewed: each month is a fresh
    /// manual UPP payment, and a lapsed CurrentPeriodEnd just shows the user as unsubscribed
    /// (GetSubscriptionStatusAsync) until they pay again.
    /// </summary>
    public class PayUniService : IPayUniService
    {
        private readonly ICreditService _creditService;
        private readonly AzureSQLDBContext _dbContext;
        private readonly PayUniOptions _options;
        private readonly ILogger<PayUniService> _logger;

        public PayUniService(
            ICreditService creditService,
            AzureSQLDBContext dbContext,
            IOptions<PayUniOptions> options,
            ILogger<PayUniService> logger)
        {
            _creditService = creditService;
            _dbContext = dbContext;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<Dictionary<string, string>> CreateSubscriptionPaymentAsync(string userId)
        {
            // Same 20-char scheme as ECPay's MerchantTradeNo (yyyyMMddHHmmss + 6 hex chars) —
            // PayUni's own sample MerTradeNo values are plain alphanumeric, no known length cap
            // documented, so reusing a format already proven to work is the safer bet.
            var tradeNo = $"{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid().ToString("N")[..6]}";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // PayUni has no per-order custom-field passthrough (unlike ECPay's CustomField1-3), so
            // the user is correlated back on callback by looking up Subscriptions.PayUniMerTradeNo
            // — persist that mapping now, before the user ever reaches PayUni's checkout page.
            await UpsertPendingSubscriptionAsync(userId, tradeNo);

            var fields = new List<(string Key, string Value)>
            {
                ("MerID", _options.MerID),
                ("MerTradeNo", tradeNo),
                ("TradeAmt", SubscriptionPlans.ProPriceTWD.ToString()),
                ("Timestamp", timestamp),
                ("ProdDesc", $"{SubscriptionPlans.ProName} (NT${SubscriptionPlans.ProPriceTWD}/月)"),
                ("ReturnURL", $"{_options.AppBaseUrl}/api/payuni/subscription/return"),
                ("NotifyURL", $"{_options.AppBaseUrl}/api/payuni/subscription/notify"),
            };

            var encryptInfo = Encrypt(BuildQueryString(fields));
            var hashInfo = Hash(encryptInfo);

            return new Dictionary<string, string>
            {
                ["MerID"] = _options.MerID,
                ["Version"] = "1.0",
                ["EncryptInfo"] = encryptInfo,
                ["HashInfo"] = hashInfo,
                ["PaymentGatewayUrl"] = $"{_options.BaseUrl}upp",
            };
        }

        public async Task<bool> HandleNotifyCallbackAsync(IFormCollection form)
        {
            var (success, fields) = TryDecryptCallback(form, "notify");
            if (!success) return false;
            return await GrantIfPaidAsync(fields, "payuni_subscription_notify");
        }

        public async Task<bool> HandleReturnCallbackAsync(IFormCollection form)
        {
            var (success, fields) = TryDecryptCallback(form, "return");
            if (!success) return false;
            // Idempotent (keyed on PayUni's TradeNo) — safe to also grant here even though
            // NotifyURL is the authoritative source, it just makes the balance update visible to
            // the user faster than waiting on the async server-to-server call.
            return await GrantIfPaidAsync(fields, "payuni_subscription_return");
        }

        public async Task<bool> CancelSubscriptionAsync(string userId)
        {
            var subscription = await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);
            if (subscription == null || subscription.Provider != "payuni")
            {
                _logger.LogWarning("No PayUni subscription found for user {UserId}", userId);
                return false;
            }

            // No API call — PayUni never had anything scheduled to auto-charge next month.
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.CancelledAt = DateTime.UtcNow;
            subscription.LastUpdated = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("PayUni subscription cancelled for user {UserId}", userId);
            return true;
        }

        public async Task<SubscriptionStatusResult> GetSubscriptionStatusAsync(string userId)
        {
            var subscription = await _dbContext.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId);
            var balance = await _creditService.GetBalanceAsync(userId);

            var isSubscribed = subscription != null
                && subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Cancelled
                && subscription.CurrentPeriodEnd > DateTime.UtcNow;

            return new SubscriptionStatusResult
            {
                IsSubscribed = isSubscribed,
                Status = subscription?.Status,
                CurrentPeriodEnd = subscription?.CurrentPeriodEnd,
                MonthlyQuota = SubscriptionPlans.ProMonthlyQuota,
                CurrentBalance = balance,
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task UpsertPendingSubscriptionAsync(string userId, string tradeNo)
        {
            var subscription = await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);
            var now = DateTime.UtcNow;

            if (subscription == null)
            {
                _dbContext.Subscriptions.Add(new Subscription
                {
                    UserId = userId,
                    Status = SubscriptionStatus.Expired, // not paid yet — flips to Active on callback
                    Provider = "payuni",
                    PayUniMerTradeNo = tradeNo,
                    LastUpdated = now,
                });
            }
            else
            {
                subscription.Provider = "payuni";
                subscription.PayUniMerTradeNo = tradeNo;
                subscription.LastUpdated = now;
            }

            await _dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// Verifies HashInfo, decrypts EncryptInfo, and parses the result into a flat field dict
        /// (TradeStatus, MerTradeNo, TradeAmt, TradeNo, ...). Shared by both notify and return —
        /// PayUni's own SDK sample code processes both callback types identically.
        /// </summary>
        private (bool Success, Dictionary<string, string> Fields) TryDecryptCallback(IFormCollection form, string source)
        {
            var encryptInfo = form["EncryptInfo"].ToString();
            var hashInfo = form["HashInfo"].ToString();

            if (string.IsNullOrEmpty(encryptInfo) || string.IsNullOrEmpty(hashInfo))
            {
                _logger.LogError("PayUni {Source} callback missing EncryptInfo/HashInfo", source);
                return (false, []);
            }

            var expectedHash = Hash(encryptInfo);
            if (!string.Equals(expectedHash, hashInfo, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("PayUni {Source} HashInfo mismatch. Expected: {Expected}, Received: {Received}",
                    source, expectedHash, hashInfo);
                return (false, []);
            }

            string plaintext;
            try
            {
                plaintext = Decrypt(encryptInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayUni {Source} EncryptInfo decrypt failed", source);
                return (false, []);
            }

            var parsed = HttpUtility.ParseQueryString(plaintext);
            var fields = parsed.AllKeys
                .Where(k => k != null)
                .ToDictionary(k => k!, k => parsed[k] ?? "");
            return (true, fields);
        }

        private async Task<bool> GrantIfPaidAsync(Dictionary<string, string> fields, string reason)
        {
            // TradeStatus: 0=待付款, 1=已付款, 2=付款失敗, 3=付款取消 — confirmed against the
            // PAYUNi_for_WooCommerce reference plugin's own status handling.
            if (fields.GetValueOrDefault("TradeStatus") != "1")
            {
                _logger.LogWarning("PayUni payment not successful. TradeStatus: {TradeStatus}, Message: {Message}",
                    fields.GetValueOrDefault("TradeStatus"), fields.GetValueOrDefault("Message"));
                return false;
            }

            var merTradeNo = fields.GetValueOrDefault("MerTradeNo", "");
            var tradeNo = fields.GetValueOrDefault("TradeNo", ""); // PayUni's own per-transaction serial (uni序號)

            if (string.IsNullOrEmpty(merTradeNo))
            {
                _logger.LogError("PayUni callback missing MerTradeNo");
                return false;
            }

            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.PayUniMerTradeNo == merTradeNo);
            if (subscription == null)
            {
                _logger.LogError("No pending PayUni subscription found for MerTradeNo {MerTradeNo}", merTradeNo);
                return false;
            }

            var now = DateTime.UtcNow;
            subscription.Status = SubscriptionStatus.Active;
            subscription.Provider = "payuni";
            subscription.CurrentPeriodEnd = now.AddMonths(1);
            subscription.StartedAt ??= now;
            subscription.LastUpdated = now;
            await _dbContext.SaveChangesAsync();

            // Each PayUni "renewal" is a brand-new UPP transaction with a fresh TradeNo, so it's
            // the correct idempotency key — same role gwsr plays for ECPay's recurring charges.
            var referenceId = !string.IsNullOrEmpty(tradeNo) ? $"payuni-{tradeNo}" : $"payuni-{merTradeNo}";
            var success = await _creditService.GrantMonthlyQuotaAsync(
                subscription.UserId, SubscriptionPlans.ProMonthlyQuota, referenceId, reason, $"PayUni trade: {merTradeNo}");

            if (success)
                _logger.LogInformation("PayUni subscription quota granted for user {UserId} ({Reason})", subscription.UserId, reason);

            return success;
        }

        private static string BuildQueryString(IEnumerable<(string Key, string Value)> fields) =>
            string.Join("&", fields.Select(f => $"{f.Key}={HttpUtility.UrlEncode(f.Value)}"));

        /// <summary>
        /// AES-256-GCM encrypt, then hex(base64(cipher) + ":::" + base64(tag)) — exact port of
        /// PayUni's official .NET SDK (payuniAPI.cs Encrypt()). Requires BouncyCastle: .NET's
        /// built-in AesGcm only accepts a 12-byte nonce, but PayUni's HashIV is 16 bytes.
        /// </summary>
        private string Encrypt(string plaintext)
        {
            const int tagLengthBytes = 16;
            var key = Encoding.UTF8.GetBytes(_options.HashKey);
            var iv = Encoding.UTF8.GetBytes(_options.HashIV);
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            var cipher = new GcmBlockCipher(new AesEngine());
            var parameters = new AeadParameters(new KeyParameter(key), tagLengthBytes * 8, iv);
            cipher.Init(true, parameters);

            var outputBuffer = new byte[cipher.GetOutputSize(plaintextBytes.Length)];
            var offset = cipher.ProcessBytes(plaintextBytes, 0, plaintextBytes.Length, outputBuffer, 0);
            cipher.DoFinal(outputBuffer, offset);

            // outputBuffer = ciphertext || tag(16 bytes)
            var cipherBytes = outputBuffer[..plaintextBytes.Length];
            var tagBytes = outputBuffer[plaintextBytes.Length..(plaintextBytes.Length + tagLengthBytes)];

            var combined = Convert.ToBase64String(cipherBytes) + ":::" + Convert.ToBase64String(tagBytes);
            return BinToHex(Encoding.UTF8.GetBytes(combined));
        }

        /// <summary>Exact port of PayUni's official .NET SDK Decrypt().</summary>
        private string Decrypt(string encryptedHex)
        {
            var combined = Encoding.UTF8.GetString(HexToBin(encryptedHex));
            var parts = combined.Split([":::"], StringSplitOptions.RemoveEmptyEntries);
            var cipherBytes = Convert.FromBase64String(parts[0]);
            var tagBytes = Convert.FromBase64String(parts[1]);

            var key = Encoding.UTF8.GetBytes(_options.HashKey);
            var iv = Encoding.UTF8.GetBytes(_options.HashIV);

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

        /// <summary>SHA256(HashKey + EncryptInfo + HashIV), uppercase hex — used both to sign
        /// outgoing requests and to verify incoming callbacks.</summary>
        private string Hash(string encryptInfo)
        {
            var raw = _options.HashKey + encryptInfo + _options.HashIV;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return BinToHex(hash).ToUpperInvariant();
        }

        private static string BinToHex(byte[] bytes) => Convert.ToHexStringLower(bytes);

        private static byte[] HexToBin(string hex) => Convert.FromHexString(hex);
    }
}
