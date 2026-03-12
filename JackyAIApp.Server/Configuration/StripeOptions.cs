namespace JackyAIApp.Server.Configuration
{
    public class StripeOptions
    {
        public const string SectionName = "Stripe";

        /// <summary>
        /// Stripe Secret Key (sk_live_... or sk_test_...)
        /// </summary>
        public required string SecretKey { get; set; }

        /// <summary>
        /// Stripe Webhook Signing Secret (whsec_...)
        /// </summary>
        public required string WebhookSecret { get; set; }

        /// <summary>
        /// Base URL for success/cancel redirects (e.g. https://jackyai.azurewebsites.net)
        /// </summary>
        public required string BaseUrl { get; set; }
    }
}
