using System.Security.Cryptography;
using System.Text;
using System.Web;
using JackyAIApp.Server.Configuration;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services
{
    public class ECPayService : IECPayService
    {
        private readonly ICreditService _creditService;
        private readonly ECPayOptions _options;
        private readonly ILogger<ECPayService> _logger;

        private static readonly (string Id, string Name, int PriceTWD, ulong Credits)[] TWDPacks =
        [
            ("starter",   "Starter Pack (500 credits)",    150,  500),
            ("popular",   "Popular Pack (1100 credits)",   300,  1100),
            ("bestvalue", "Best Value Pack (2400 credits)", 600,  2400),
        ];

        public ECPayService(
            ICreditService creditService,
            IOptions<ECPayOptions> options,
            ILogger<ECPayService> logger)
        {
            _creditService = creditService;
            _options = options.Value;
            _logger = logger;
        }

        public Task<Dictionary<string, string>> CreatePaymentAsync(string userId, string packId)
        {
            var pack = TWDPacks.FirstOrDefault(p => p.Id == packId);
            if (pack == default)
                throw new ArgumentException($"Unknown pack: {packId}");

            var tradeNo = $"JA{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid().ToString("N")[..6]}";
            var tradeDate = DateTime.UtcNow.AddHours(8).ToString("yyyy/MM/dd HH:mm:ss"); // ECPay uses UTC+8

            var parameters = new SortedDictionary<string, string>
            {
                ["MerchantID"] = _options.MerchantID,
                ["MerchantTradeNo"] = tradeNo,
                ["MerchantTradeDate"] = tradeDate,
                ["PaymentType"] = "aio",
                ["TotalAmount"] = pack.PriceTWD.ToString(),
                ["TradeDesc"] = "JackyAI Credits",
                ["ItemName"] = pack.Name,
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

            // Verify CheckMacValue
            var receivedMac = formDict.GetValueOrDefault("CheckMacValue", "");
            var paramsForMac = new SortedDictionary<string, string>(
                formDict.Where(kv => kv.Key != "CheckMacValue")
                    .ToDictionary(kv => kv.Key, kv => kv.Value));

            var expectedMac = GenerateCheckMacValue(paramsForMac);
            if (!string.Equals(receivedMac, expectedMac, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("ECPay CheckMacValue mismatch. Expected: {Expected}, Received: {Received}",
                    expectedMac, receivedMac);
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
