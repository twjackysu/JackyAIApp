namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// A purchasable credit pack. Carries both USD cents (PayPal) and TWD (ECPay) prices —
    /// each provider settles in its own currency, so neither is derived from the other via a
    /// floating FX rate (that would make the amount actually charged unpredictable).
    /// </summary>
    public class CreditPack
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public long PriceInCents { get; set; }
        public int PriceTWD { get; set; }
        public ulong Credits { get; set; }
        public string? Badge { get; set; }
    }

    /// <summary>
    /// Single source of truth for the credit packs offered across every payment method
    /// (PayPal, ECPay, …). Kept payment-provider agnostic on purpose.
    /// </summary>
    public static class CreditPacks
    {
        public static readonly CreditPack[] All =
        [
            new() { Id = "starter",   Name = "入門方案", PriceInCents = 500,  PriceTWD = 150, Credits = 500,  Badge = null },
            new() { Id = "popular",   Name = "人氣方案", PriceInCents = 1000, PriceTWD = 300, Credits = 1100, Badge = "多送 10%" },
            new() { Id = "bestvalue", Name = "超值方案", PriceInCents = 2000, PriceTWD = 600, Credits = 2400, Badge = "多送 20%" },
        ];
    }
}
