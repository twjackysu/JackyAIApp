using JackyAIApp.Server.Data.Models.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// Classifies a holding's market exposure multiplier from its name (primary) and,
    /// for TW listed ETFs, its code suffix (fallback). No hard-coded ticker list.
    ///
    /// <list type="bullet">
    ///   <item>Leveraged 2x (正2 / 槓桿 / code …L) → 2</item>
    ///   <item>Inverse (反1 / 反向 / code …R) → -1  (反2 → -2)</item>
    ///   <item>Bond / money-market (含「債」/「貨幣」/ code …B) → 0, i.e. treated like cash</item>
    ///   <item>Cash → 0; everything else → 1</item>
    /// </list>
    /// </summary>
    public static class PortfolioExposure
    {
        public static decimal Factor(AssetType type, string? name, string? symbol, string? market)
        {
            if (type == AssetType.CASH) return 0m;
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
                if (last == 'B') return 0m;   // 債券
            }

            return 1m;
        }
    }
}
