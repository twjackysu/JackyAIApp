using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// How much one save of holdings (<c>PUT api/portfolio/holdings</c>) may hold, and how many snapshots a reader may keep
    /// (D-165). Every text field is nvarchar(max) and each date is a snapshot of its own, so without these one account could
    /// fill the database with a few requests (30 MB names, a new date each time) — and a holding's name also goes into the AI
    /// summary's prompt. Far above what a person's portfolio needs: the editor never comes near them.
    /// </summary>
    public static class HoldingsLimits
    {
        /// <summary>The save's request body (<c>[RequestSizeLimit]</c> on the action): 300 rows at their longest fit well inside.</summary>
        public const int MaxRequestBytes = 256 * 1024;

        public const int MaxPositions = 300;

        /// <summary>The same length the AI's <c>update_holding</c> takes for a symbol.</summary>
        public const int MaxSymbolLength = 40;

        public const int MaxNameLength = 100;

        /// <summary>Market, currency and valuation method are short codes or labels ("TW", "USD", "買價扣除折舊").</summary>
        public const int MaxCodeLength = 20;

        /// <summary>Snapshots a reader may keep: a save a day for more than five years. Saving again on a date already
        /// kept replaces that snapshot, so only a new date counts.</summary>
        public const int MaxSnapshots = 2000;

        public const string TooManySnapshotsMessage =
            "投資組合的歷史快照已達上限（2000 筆），請先刪除一些舊的快照再儲存新的日期。";

        /// <summary>Why <paramref name="req"/> cannot be saved, or null when it may.</summary>
        public static string? Problem(SaveHoldingsRequest req)
        {
            if (req.Positions is null) return "沒有持倉資料。";
            if (req.Positions.Count > MaxPositions) return $"一次最多儲存 {MaxPositions} 筆持倉。";
            foreach (var p in req.Positions)
            {
                if (p is null) return "持倉資料有空白的一筆。";
                if (TooLong(p.Symbol, MaxSymbolLength)) return $"代號最多 {MaxSymbolLength} 個字。";
                if (TooLong(p.Name, MaxNameLength)) return $"名稱最多 {MaxNameLength} 個字。";
                if (TooLong(p.Market, MaxCodeLength) || TooLong(p.Currency, MaxCodeLength) ||
                    TooLong(p.CostCurrency, MaxCodeLength) || TooLong(p.ValuationMethod, MaxCodeLength))
                    return $"市場、幣別與估價方式最多 {MaxCodeLength} 個字。";
            }
            return null;
        }

        private static bool TooLong(string? value, int max) => value is not null && value.Length > max;
    }
}
