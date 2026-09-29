using JackyAIApp.Server.Data.Models.Portfolio;
using System.Text.RegularExpressions;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// Classifies a holding's market exposure multiplier from its name (primary) and,
    /// for TW listed ETFs, its code suffix (fallback). No hard-coded TW ticker list — US
    /// leveraged ETFs are the one exception (see <see cref="KnownUsLeveragedTickers"/>), added
    /// because there's no free structured API for this (Yahoo's quoteSummary needs an auth
    /// crumb we don't have; Finnhub's ETF profile endpoint is paid-tier only).
    ///
    /// <list type="bullet">
    ///   <item>Leveraged 2x (正2 / 槓桿 / code …L) → 2</item>
    ///   <item>Inverse (反1 / 反向 / code …R) → -1  (反2 → -2)</item>
    ///   <item>Bond / money-market (含「債」/「貨幣」/ code …B or …D, passive/active bond ETF) → 0, i.e. treated like cash</item>
    ///   <item>US leveraged/inverse ETF (name pattern or known ticker, e.g. TQQQ/QLD/NVDL) → per-ticker</item>
    ///   <item>US capital-efficient fund whose stacked exposure is all equity (NTSD) → per-ticker</item>
    ///   <item>Cash / Liability → 0; everything else → 1</item>
    /// </list>
    /// </summary>
    public static class PortfolioExposure
    {
        // Small curated backstop for well-known ProShares/Direxion products whose name doesn't
        // spell the multiplier out as a plain digit (e.g. "ProShares UltraPro QQQ" relies on the
        // "UltraPro" keyword below, which is reliable — this list exists for symbols we've
        // hand-verified where that isn't enough). Expand as needed; unmatched symbols fall
        // through to the name-pattern check.
        private static readonly Dictionary<string, decimal> KnownUsLeveragedTickers =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["TQQQ"] = 3m,
                ["SQQQ"] = -3m,
                ["QLD"] = 2m,
                ["QID"] = -2m,
                ["UPRO"] = 3m,
                ["SPXU"] = -3m,
                ["SPXL"] = 3m,
                ["SPXS"] = -3m,
                ["SOXL"] = 3m,
                ["SOXS"] = -3m,
                ["TNA"] = 3m,
                ["TZA"] = -3m,
                ["UDOW"] = 3m,
                ["SDOW"] = -3m,
                ["TMF"] = 3m,
                ["TMV"] = -3m,

                // WisdomTree Efficient U.S. Plus International Equity Fund: per $1, $0.90 of U.S.
                // large-cap stock plus $0.60 notional of MSCI EAFE futures — 150% equity exposure.
                // Not a daily-reset leveraged fund, and its name carries no multiplier, so the
                // name patterns below can't see it.
                //
                // Only list a "90/60" fund here when both legs are equity. NTSX / NTSI / NTSE stack
                // Treasury futures on the stock leg, and bonds count as 0 in this factor, so their
                // equity exposure is 0.9 — not 1.5.
                ["NTSD"] = 1.5m,
            };

        public static decimal Factor(AssetType type, string? name, string? symbol, string? market)
        {
            if (type is AssetType.CASH or AssetType.LIABILITY or AssetType.REAL_ESTATE) return 0m;
            name ??= "";
            symbol ??= "";

            // Name-based signals carry across both TW ETFs and mutual funds.
            if (name.Contains("反2") || name.Contains("反二")) return -2m;
            if (name.Contains("正2") || name.Contains("正二") || name.Contains("槓桿")) return 2m;
            if (name.Contains("反1") || name.Contains("反一") || name.Contains("反向")) return -1m;
            // "債" covers 債券/公債/美債/投資級債/金融債; 貨幣 = money-market ≈ cash.
            if (name.Contains('債') || name.Contains("貨幣")) return 0m;

            // Suffix fallback for TW listed ETFs when the name didn't say (codes end in a letter;
            // ordinary equities are purely numeric so this never misfires on them).
            if (type == AssetType.STOCK && market == "TW" && symbol.Length > 0)
            {
                var last = char.ToUpperInvariant(symbol[^1]);
                if (last == 'L') return 2m;   // 槓桿
                if (last == 'R') return -1m;  // 反向
                if (last == 'B') return 0m;   // 債券ETF（被動）
                if (last == 'D') return 0m;   // 債券ETF（主動）
            }

            if (type == AssetType.STOCK && market == "US")
            {
                if (symbol.Length > 0 && KnownUsLeveragedTickers.TryGetValue(symbol, out var known))
                    return known;

                var fromName = ClassifyUsLeveragedName(name);
                if (fromName.HasValue) return fromName.Value;
            }

            return 1m;
        }

        /// <summary>
        /// SEC's Fund Names Rule requires leveraged/inverse funds to disclose the multiplier and
        /// direction in the fund name, so this is a regulatory disclosure convention, not
        /// marketing copy — reasonably stable to pattern-match. Covers ProShares' "Ultra"/
        /// "UltraPro"/"UltraShort" branding (which omits an explicit digit) and Direxion/
        /// GraniteShares-style names that spell the multiplier out directly ("2x", "3X Shares").
        /// </summary>
        private static decimal? ClassifyUsLeveragedName(string name)
        {
            var isBearish = Regex.IsMatch(name, @"\b(Bear|Short|Inverse)\b", RegexOptions.IgnoreCase);

            if (Regex.IsMatch(name, @"\bUltraPro\b", RegexOptions.IgnoreCase))
                return isBearish ? -3m : 3m;
            if (Regex.IsMatch(name, @"\bUltraShort\b", RegexOptions.IgnoreCase))
                return -2m;
            if (Regex.IsMatch(name, @"\bUltra\b", RegexOptions.IgnoreCase))
                return isBearish ? -2m : 2m;

            var digitMatch = Regex.Match(name, @"\b(\d+(?:\.\d+)?)\s*[Xx]\b");
            if (digitMatch.Success && decimal.TryParse(digitMatch.Groups[1].Value, out var multiplier) && multiplier > 0)
                return isBearish ? -multiplier : multiplier;

            // Plain inverse with no multiplier spelled out, e.g. "ProShares Short S&P500".
            if (isBearish) return -1m;

            return null;
        }
    }
}
