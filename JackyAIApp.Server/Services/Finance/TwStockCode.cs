using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// The one definition of what a Taiwan stock code looks like, shared by market routing
    /// (<c>MarketDataProviderFactory</c>) and input resolution (<c>StockCodeResolver</c>) so the two
    /// cannot drift apart.
    /// </summary>
    internal static class TwStockCode
    {
        // 4-6 digits, optionally ending with a letter (e.g. "2330", "6547", "00878", "00631L").
        private static readonly Regex Pattern = new(@"^\d{4,6}[A-Za-z]?$", RegexOptions.Compiled);

        /// <summary>
        /// True when <paramref name="input"/>, trimmed, is shaped like a TW code; <paramref name="code"/> is
        /// then the trimmed, upper-cased form (<c>00631l</c> → <c>00631L</c>, as TWStockLib spells ETF codes).
        /// </summary>
        public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? code)
        {
            var trimmed = input?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && Pattern.IsMatch(trimmed))
            {
                code = trimmed.ToUpperInvariant();
                return true;
            }

            code = null;
            return false;
        }
    }
}
