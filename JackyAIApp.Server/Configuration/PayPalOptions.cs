namespace JackyAIApp.Server.Configuration
{
    public class PayPalOptions
    {
        public const string SectionName = "PayPal";
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }
        /// <summary>
        /// "https://api-m.sandbox.paypal.com" or "https://api-m.paypal.com"
        /// </summary>
        public required string BaseUrl { get; set; }
        public required string AppBaseUrl { get; set; }
    }
}
