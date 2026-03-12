namespace JackyAIApp.Server.Configuration
{
    public class ECPayOptions
    {
        public const string SectionName = "ECPay";
        public required string MerchantID { get; set; }
        public required string HashKey { get; set; }
        public required string HashIV { get; set; }
        /// <summary>
        /// "https://payment-stage.ecpay.com.tw" or "https://payment.ecpay.com.tw"
        /// </summary>
        public required string BaseUrl { get; set; }
        public required string AppBaseUrl { get; set; }
    }
}
