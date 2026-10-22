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

        /// <summary>
        /// Warrants are six characters: TWSE's 03xxxx–08xxxx, TPEx's 7xxxxx (認售 and other kinds
        /// end in a letter, e.g. 72414U). Nobody records a warrant as a holding here, and TPEx alone
        /// lists ~11,000 of them, which would bury the stocks in search. Other six-character
        /// symbols stay: 00xxxx ETFs (00981A), 01xxxT REITs, 02xxxx ETNs, 91xxxx DRs.
        /// </summary>
        internal static bool IsWarrant(string symbol) =>
            symbol.Length == 6 && (symbol[0] == '7' || (symbol[0] == '0' && symbol[1] is >= '3' and <= '8'));
    }
}
