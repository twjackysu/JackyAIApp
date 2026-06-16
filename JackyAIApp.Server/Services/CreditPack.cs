namespace JackyAIApp.Server.Services
{
    /// <summary>A purchasable credit pack. USD pricing in cents is the canonical amount.</summary>
    public class CreditPack
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public long PriceInCents { get; set; }
        public ulong Credits { get; set; }
        public string? Badge { get; set; }
    }

    /// <summary>
    /// Single source of truth for the credit packs offered across every payment method
    /// (PayPal, bank transfer, …). Kept payment-provider agnostic on purpose.
    /// </summary>
    public static class CreditPacks
    {
        public static readonly CreditPack[] All =
        [
            new() { Id = "starter",   Name = "Starter Pack",    PriceInCents = 500,  Credits = 500,  Badge = null },
            new() { Id = "popular",   Name = "Popular Pack",    PriceInCents = 1000, Credits = 1100, Badge = "10% Bonus" },
            new() { Id = "bestvalue", Name = "Best Value Pack", PriceInCents = 2000, Credits = 2400, Badge = "20% Bonus" },
        ];
    }
}
