using JackyAIApp.Server.Configuration;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace JackyAIApp.Server.Services
{
    public class CreditPack
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public long PriceInCents { get; set; }
        public ulong Credits { get; set; }
        public string? Badge { get; set; }
    }

    public class StripeService : IStripeService
    {
        private readonly ICreditService _creditService;
        private readonly StripeOptions _options;
        private readonly ILogger<StripeService> _logger;

        public static readonly CreditPack[] CreditPacks =
        [
            new() { Id = "starter",   Name = "Starter Pack",    PriceInCents = 500,  Credits = 500,  Badge = null },
            new() { Id = "popular",   Name = "Popular Pack",    PriceInCents = 1000, Credits = 1100, Badge = "10% Bonus" },
            new() { Id = "bestvalue", Name = "Best Value Pack", PriceInCents = 2000, Credits = 2400, Badge = "20% Bonus" },
        ];

        public StripeService(
            ICreditService creditService,
            IOptions<StripeOptions> options,
            ILogger<StripeService> logger)
        {
            _creditService = creditService;
            _options = options.Value;
            _logger = logger;

            StripeConfiguration.ApiKey = _options.SecretKey;
        }

        public async Task<string> CreateCheckoutSessionAsync(string userId, string packId)
        {
            var pack = CreditPacks.FirstOrDefault(p => p.Id == packId)
                ?? throw new ArgumentException($"Unknown pack: {packId}");

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = ["card"],
                LineItems =
                [
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "usd",
                            UnitAmount = pack.PriceInCents,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = pack.Name,
                                Description = $"{pack.Credits:N0} credits for JackyAI",
                            },
                        },
                        Quantity = 1,
                    },
                ],
                Mode = "payment",
                SuccessUrl = $"{_options.BaseUrl}/credits?session_id={{CHECKOUT_SESSION_ID}}&status=success",
                CancelUrl = $"{_options.BaseUrl}/credits?status=cancelled",
                ClientReferenceId = userId,
                Metadata = new Dictionary<string, string>
                {
                    ["packId"] = pack.Id,
                    ["credits"] = pack.Credits.ToString(),
                    ["userId"] = userId,
                },
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return session.Url;
        }

        public async Task<bool> HandleWebhookAsync(string json, string signature)
        {
            try
            {
                var stripeEvent = EventUtility.ConstructEvent(json, signature, _options.WebhookSecret);

                if (stripeEvent.Type == EventTypes.CheckoutSessionCompleted)
                {
                    var session = stripeEvent.Data.Object as Session;
                    if (session == null) return false;

                    var userId = session.ClientReferenceId;
                    var packId = session.Metadata.GetValueOrDefault("packId", "");
                    var creditsStr = session.Metadata.GetValueOrDefault("credits", "0");

                    if (string.IsNullOrEmpty(userId) || !ulong.TryParse(creditsStr, out var credits) || credits == 0)
                    {
                        _logger.LogWarning("Invalid webhook metadata: userId={UserId}, credits={Credits}", userId, creditsStr);
                        return false;
                    }

                    // Idempotency: use payment intent ID as reference
                    var referenceId = session.PaymentIntentId;

                    var success = await _creditService.AddCreditsAsync(
                        userId,
                        credits,
                        "purchase",
                        $"Purchased {packId} pack",
                        $"Stripe session: {session.Id}",
                        referenceId
                    );

                    if (success)
                    {
                        _logger.LogInformation("Credits added: {Credits} for user {UserId} (pack: {PackId})", credits, userId, packId);
                    }
                    else
                    {
                        _logger.LogError("Failed to add credits for user {UserId}", userId);
                    }

                    return success;
                }

                return true; // Acknowledge other event types
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe webhook signature verification failed");
                return false;
            }
        }
    }
}
