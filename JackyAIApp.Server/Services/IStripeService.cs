namespace JackyAIApp.Server.Services
{
    public interface IStripeService
    {
        /// <summary>
        /// Creates a Stripe Checkout Session for purchasing credits.
        /// </summary>
        /// <param name="userId">The authenticated user ID</param>
        /// <param name="packId">The credit pack identifier (starter, popular, bestvalue)</param>
        /// <returns>The Checkout Session URL to redirect the user to</returns>
        Task<string> CreateCheckoutSessionAsync(string userId, string packId);

        /// <summary>
        /// Handles a Stripe webhook event (checkout.session.completed).
        /// </summary>
        /// <param name="json">Raw JSON body from Stripe</param>
        /// <param name="signature">Stripe-Signature header value</param>
        /// <returns>True if handled successfully</returns>
        Task<bool> HandleWebhookAsync(string json, string signature);
    }
}
