using System.Security.Cryptography;
using System.Text;
using System.Web;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services
{
    public class ECPayService : IECPayService
    {
        private readonly ICreditService _creditService;
        private readonly AzureSQLDBContext _dbContext;
        private readonly ECPayOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ECPayService> _logger;

        public ECPayService(
            ICreditService creditService,
            AzureSQLDBContext dbContext,
            IOptions<ECPayOptions> options,
            HttpClient httpClient,
            ILogger<ECPayService> logger)
        {
            _creditService = creditService;
            _dbContext = dbContext;
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        public Task<Dictionary<string, string>> CreatePaymentAsync(string userId, string packId)
        {
            var pack = CreditPacks.All.FirstOrDefault(p => p.Id == packId);
            if (pack == null)
                throw new ArgumentException($"Unknown pack: {packId}");

            // ECPay caps MerchantTradeNo at 20 chars. yyyyMMddHHmmss (14) + 6 hex chars = 20 exactly;
            // no room for a prefix (the previous "JA" prefix pushed this to 22 and every payment
            // failed with ECPay error 1000500).
            var tradeNo = $"{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid().ToString("N")[..6]}";
            var tradeDate = DateTime.UtcNow.AddHours(8).ToString("yyyy/MM/dd HH:mm:ss"); // ECPay uses UTC+8

            var parameters = new SortedDictionary<string, string>
            {
                ["MerchantID"] = _options.MerchantID,
                ["MerchantTradeNo"] = tradeNo,
                ["MerchantTradeDate"] = tradeDate,
                ["PaymentType"] = "aio",
                ["TotalAmount"] = pack.PriceTWD.ToString(),
                ["TradeDesc"] = "JackyAI Credits",
                ["ItemName"] = $"{pack.Name} ({pack.Credits} credits)",
                ["ReturnURL"] = $"{_options.AppBaseUrl}/api/ecpay/callback",
                ["ClientBackURL"] = $"{_options.AppBaseUrl}/credits?status=success&provider=ecpay",
                ["ChoosePayment"] = "Credit",
                ["EncryptType"] = "1",
                ["CustomField1"] = userId,
                ["CustomField2"] = packId,
                ["CustomField3"] = pack.Credits.ToString(),
            };

            parameters["CheckMacValue"] = GenerateCheckMacValue(parameters);

            return Task.FromResult(new Dictionary<string, string>(parameters)
            {
                ["PaymentGatewayUrl"] = $"{_options.BaseUrl}/Cashier/AioCheckOut/V5"
            });
        }

        public async Task<bool> HandleCallbackAsync(IFormCollection form)
        {
            var formDict = form.ToDictionary(x => x.Key, x => x.Value.ToString());

            if (!VerifyCheckMacValue(formDict, out var mismatchLog))
            {
                _logger.LogError("{Log}", mismatchLog);
                return false;
            }

            // Check payment status
            var rtnCode = formDict.GetValueOrDefault("RtnCode", "");
            if (rtnCode != "1")
            {
                _logger.LogWarning("ECPay payment not successful. RtnCode: {RtnCode}", rtnCode);
                return false;
            }

            var userId = formDict.GetValueOrDefault("CustomField1", "");
            var packId = formDict.GetValueOrDefault("CustomField2", "");
            var creditsStr = formDict.GetValueOrDefault("CustomField3", "0");
            var tradeNo = formDict.GetValueOrDefault("MerchantTradeNo", "");

            if (string.IsNullOrEmpty(userId) || !ulong.TryParse(creditsStr, out var credits) || credits == 0)
            {
                _logger.LogError("Invalid ECPay callback data: userId={UserId}, credits={Credits}", userId, creditsStr);
                return false;
            }

            var success = await _creditService.AddCreditsAsync(
                userId, credits, "purchase",
                $"Purchased {packId} pack via ECPay",
                $"ECPay trade: {tradeNo}",
                tradeNo);

            if (success)
                _logger.LogInformation("ECPay credits added: {Credits} for user {UserId}", credits, userId);

            return success;
        }

        // ── Subscription (信用卡定期定額) ──────────────────────────────────────

        public Task<Dictionary<string, string>> CreateSubscriptionPaymentAsync(string userId)
        {
            var tradeNo = $"{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid().ToString("N")[..6]}";
            var tradeDate = DateTime.UtcNow.AddHours(8).ToString("yyyy/MM/dd HH:mm:ss");

            var parameters = new SortedDictionary<string, string>
            {
                ["MerchantID"] = _options.MerchantID,
                ["MerchantTradeNo"] = tradeNo,
                ["MerchantTradeDate"] = tradeDate,
                ["PaymentType"] = "aio",
                ["TotalAmount"] = SubscriptionPlans.ProPriceTWD.ToString(),
                ["TradeDesc"] = "JackyAI Pro Subscription",
                ["ItemName"] = $"{SubscriptionPlans.ProName} (NT${SubscriptionPlans.ProPriceTWD}/月)",
                // Distinct from the one-time-pack ReturnURL (/api/ecpay/callback) — the two flows
                // credit the user completely differently (grant-and-reset vs additive top-up), so
                // they must never be routed through the same handler.
                ["ReturnURL"] = $"{_options.AppBaseUrl}/api/ecpay/subscription/callback",
                ["ClientBackURL"] = $"{_options.AppBaseUrl}/credits?status=success&provider=ecpay&type=subscription",
                ["ChoosePayment"] = "Credit", // only credit cards support ECPay's periodic billing
                ["EncryptType"] = "1",
                ["CustomField1"] = userId,
                ["CustomField2"] = SubscriptionPlans.ProId,
                // Only sends gwsr (授權交易單號) on the ReturnURL notification if requested — needed
                // so the first charge and every later PeriodReturnURL charge share the same kind of
                // per-charge identifier for GrantMonthlyQuotaAsync's idempotency check.
                ["NeedExtraPaidInfo"] = "Y",
                // Periodic-billing fields (信用卡定期定額) — every period charges the same amount
                // monthly. ExecTimes=999 is ECPay's max for PeriodType=M (~83 years); there's no
                // benefit to a lower number and it avoids ever hitting the cap and silently lapsing
                // a still-paying subscriber.
                ["PeriodAmount"] = SubscriptionPlans.ProPriceTWD.ToString(),
                ["PeriodType"] = "M",
                ["Frequency"] = "1",
                ["ExecTimes"] = "999",
                ["PeriodReturnURL"] = $"{_options.AppBaseUrl}/api/ecpay/subscription/period-callback",
            };

            parameters["CheckMacValue"] = GenerateCheckMacValue(parameters);

            return Task.FromResult(new Dictionary<string, string>(parameters)
            {
                ["PaymentGatewayUrl"] = $"{_options.BaseUrl}/Cashier/AioCheckOut/V5"
            });
        }

        public async Task<bool> HandleSubscriptionFirstChargeCallbackAsync(IFormCollection form)
        {
            var formDict = form.ToDictionary(x => x.Key, x => x.Value.ToString());

            if (!VerifyCheckMacValue(formDict, out var mismatchLog))
            {
                _logger.LogError("{Log}", mismatchLog);
                return false;
            }

            if (formDict.GetValueOrDefault("RtnCode", "") != "1")
            {
                _logger.LogWarning("ECPay subscription first charge not successful. RtnCode: {RtnCode}",
                    formDict.GetValueOrDefault("RtnCode", ""));
                return false;
            }

            var userId = formDict.GetValueOrDefault("CustomField1", "");
            var tradeNo = formDict.GetValueOrDefault("MerchantTradeNo", "");
            var gwsr = formDict.GetValueOrDefault("gwsr", "");

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tradeNo))
            {
                _logger.LogError("Invalid ECPay subscription callback data: userId={UserId}, tradeNo={TradeNo}", userId, tradeNo);
                return false;
            }

            await UpsertSubscriptionAsync(userId, tradeNo, SubscriptionStatus.Active);
            return await GrantPeriodQuotaAsync(userId, gwsr, tradeNo, "ecpay_subscription_first_charge");
        }

        public async Task<bool> HandleSubscriptionPeriodCallbackAsync(IFormCollection form)
        {
            var formDict = form.ToDictionary(x => x.Key, x => x.Value.ToString());

            if (!VerifyCheckMacValue(formDict, out var mismatchLog))
            {
                _logger.LogError("{Log}", mismatchLog);
                return false;
            }

            if (formDict.GetValueOrDefault("RtnCode", "") != "1")
            {
                _logger.LogWarning("ECPay subscription period charge not successful. RtnCode: {RtnCode}, RtnMsg: {RtnMsg}",
                    formDict.GetValueOrDefault("RtnCode", ""), formDict.GetValueOrDefault("RtnMsg", ""));
                // A failed period charge is not fatal here — ECPay retries on its own schedule
                // and will just call again (or the subscriber's CurrentPeriodEnd eventually
                // passes and they lapse to the free tier on their own; no explicit action needed).
                return false;
            }

            var userId = formDict.GetValueOrDefault("CustomField1", "");
            var tradeNo = formDict.GetValueOrDefault("MerchantTradeNo", "");
            var gwsr = formDict.GetValueOrDefault("gwsr", "");

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tradeNo))
            {
                _logger.LogError("Invalid ECPay period callback data: userId={UserId}, tradeNo={TradeNo}", userId, tradeNo);
                return false;
            }

            await UpsertSubscriptionAsync(userId, tradeNo, SubscriptionStatus.Active);
            return await GrantPeriodQuotaAsync(userId, gwsr, tradeNo, "ecpay_subscription_period_charge");
        }

        public async Task<bool> CancelSubscriptionAsync(string userId)
        {
            var subscription = await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);
            if (subscription?.EcpayMerchantTradeNo == null)
            {
                _logger.LogWarning("No active ECPay subscription trade number found for user {UserId}", userId);
                return false;
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var parameters = new SortedDictionary<string, string>
            {
                ["MerchantID"] = _options.MerchantID,
                ["MerchantTradeNo"] = subscription.EcpayMerchantTradeNo,
                ["Action"] = "Cancel",
                ["TimeStamp"] = timestamp,
            };
            parameters["CheckMacValue"] = GenerateCheckMacValue(parameters);

            var content = new FormUrlEncodedContent(parameters);
            var response = await _httpClient.PostAsync(
                $"{_options.BaseUrl}/Cashier/CreditCardPeriodAction", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            // ECPay's CreditCardPeriodAction responds as a query-string-shaped body
            // (RtnCode=...&RtnMsg=...&...), same style as its other server-to-server APIs.
            var responseFields = HttpUtility.ParseQueryString(responseBody);
            var rtnCode = responseFields["RtnCode"];

            if (rtnCode != "1")
            {
                _logger.LogError(
                    "ECPay subscription cancel failed for user {UserId}: {RtnCode} - {RtnMsg}. Raw: {Body}",
                    userId, rtnCode, responseFields["RtnMsg"], responseBody);
                return false;
            }

            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.CancelledAt = DateTime.UtcNow;
            subscription.LastUpdated = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("ECPay subscription cancelled for user {UserId}", userId);
            return true;
        }

        public async Task<SubscriptionStatusResult> GetSubscriptionStatusAsync(string userId)
        {
            var subscription = await _dbContext.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId);
            var balance = await _creditService.GetBalanceAsync(userId);

            // "Subscribed" covers cancelled-but-still-within-the-paid-period, not just Active —
            // the user already paid for this period, so access shouldn't drop the moment they
            // click cancel.
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

        private async Task UpsertSubscriptionAsync(string userId, string tradeNo, string status)
        {
            var subscription = await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);
            var now = DateTime.UtcNow;

            if (subscription == null)
            {
                _dbContext.Subscriptions.Add(new Subscription
                {
                    UserId = userId,
                    Status = status,
                    Provider = "ecpay",
                    CurrentPeriodEnd = now.AddMonths(1),
                    EcpayMerchantTradeNo = tradeNo,
                    StartedAt = now,
                    LastUpdated = now,
                });
            }
            else
            {
                subscription.Status = status;
                subscription.Provider = "ecpay";
                subscription.CurrentPeriodEnd = now.AddMonths(1);
                subscription.EcpayMerchantTradeNo = tradeNo;
                subscription.StartedAt ??= now;
                subscription.LastUpdated = now;
            }

            await _dbContext.SaveChangesAsync();
        }

        private async Task<bool> GrantPeriodQuotaAsync(string userId, string gwsr, string tradeNo, string reason)
        {
            // gwsr is ECPay's per-authorization transaction number — unique per charge (including
            // the first, since CreateSubscriptionPaymentAsync sets NeedExtraPaidInfo=Y), so it's
            // the correct idempotency key across the whole subscription lifecycle. TradeNo alone
            // would NOT work here — it identifies the recurring *order*, which stays the same
            // across every period, so using it as the referenceId would make every period after
            // the first a no-op.
            var referenceId = !string.IsNullOrEmpty(gwsr) ? $"ecpay-period-{gwsr}" : $"ecpay-period-{tradeNo}-{DateTime.UtcNow:yyyyMMdd}";

            var success = await _creditService.GrantMonthlyQuotaAsync(
                userId, SubscriptionPlans.ProMonthlyQuota, referenceId, reason, $"ECPay trade: {tradeNo}");

            if (success)
                _logger.LogInformation("ECPay subscription quota granted for user {UserId} ({Reason})", userId, reason);

            return success;
        }

        private bool VerifyCheckMacValue(Dictionary<string, string> formDict, out string log)
        {
            var receivedMac = formDict.GetValueOrDefault("CheckMacValue", "");
            var paramsForMac = new SortedDictionary<string, string>(
                formDict.Where(kv => kv.Key != "CheckMacValue")
                    .ToDictionary(kv => kv.Key, kv => kv.Value));

            var expectedMac = GenerateCheckMacValue(paramsForMac);
            if (string.Equals(receivedMac, expectedMac, StringComparison.OrdinalIgnoreCase))
            {
                log = "";
                return true;
            }

            log = $"ECPay CheckMacValue mismatch. Expected: {expectedMac}, Received: {receivedMac}";
            return false;
        }

        private string GenerateCheckMacValue(SortedDictionary<string, string> parameters)
        {
            var raw = string.Join("&", parameters.Select(kv => $"{kv.Key}={kv.Value}"));
            raw = $"HashKey={_options.HashKey}&{raw}&HashIV={_options.HashIV}";
            raw = HttpUtility.UrlEncode(raw).ToLower();

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return BitConverter.ToString(hash).Replace("-", "").ToUpper();
        }
    }
}
