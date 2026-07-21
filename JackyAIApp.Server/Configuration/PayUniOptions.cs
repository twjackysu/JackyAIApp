namespace JackyAIApp.Server.Configuration
{
    public class PayUniOptions
    {
        public const string SectionName = "PayUni";
        public required string MerID { get; set; }
        public required string HashKey { get; set; }
        public required string HashIV { get; set; }
        /// <summary>
        /// "https://sandbox-api.payuni.com.tw/api/" or "https://api.payuni.com.tw/api/" — trade
        /// type (e.g. "upp", "credit", "trade/query") is appended directly, no separating slash.
        /// </summary>
        public required string BaseUrl { get; set; }
        public required string AppBaseUrl { get; set; }
    }
}
