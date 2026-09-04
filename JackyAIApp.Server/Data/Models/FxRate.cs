using System.ComponentModel.DataAnnotations;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// One published foreign-exchange rate for one currency on one date, shared across all users.
    /// Like <see cref="DailyStockPrice"/>, rows are only ever written for dates whose rate has
    /// already been published, and a published rate never changes — so once a row exists it is
    /// never refetched.
    ///
    /// Exists because different holdings need different rates. A fund bought through 基富通 is
    /// settled by 集保結算所 at a rate it negotiates with 華南銀行, so that is the only rate at
    /// which the holder can actually convert back to TWD — and it is what 基富通 itself displays.
    /// Valuing a fund at a mid-market rate instead put this app roughly 0.5% away from the figure
    /// the user sees in their own broker's app. <see cref="Source"/> keeps both kinds side by side
    /// rather than forcing one convention onto every asset type.
    /// </summary>
    public class FxRate
    {
        [Key]
        public int Id { get; set; }

        /// <summary>The date the rate was published FOR, as the source labels it (not fetch time).</summary>
        public required DateOnly RateDate { get; set; }

        /// <summary>ISO 4217 code of the foreign currency, quoted against TWD. E.g. "USD".</summary>
        public required string Currency { get; set; }

        /// <summary>Which convention this rate follows — see <see cref="FxRateSources"/>.</summary>
        public required string Source { get; set; }

        /// <summary>
        /// Foreign currency → TWD. The rate to VALUE a holding at: it is what the holder receives
        /// on the way out. Do not assume this is below <see cref="SellRate"/> — 集保 published
        /// 2026/08/21 with the two inverted, so any code deriving a spread from the pair has to
        /// tolerate a negative one.
        /// </summary>
        public decimal BuyRate { get; set; }

        /// <summary>TWD → foreign currency. Stored for completeness; valuation uses BuyRate.</summary>
        public decimal SellRate { get; set; }

        public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    }

    public static class FxRateSources
    {
        /// <summary>
        /// 集保結算所 基金交易平台 (fundclear.com.tw). Published once per business day after the
        /// settlement batch runs — early afternoon, but deliberately not at a fixed time, so the
        /// only reliable way to know whether a date is out is to ask. Weekends and holidays have
        /// no rate at all. Applies to FUND holdings.
        /// </summary>
        public const string Tdcc = "TDCC";

        /// <summary>
        /// 國泰世華銀行 published board rate (cathaybk.com.tw). What 國泰 複委託 settles US holdings
        /// at: its own field definitions state 總預估現值 = 股數 × 最新價 × 昨收匯率, so the rate
        /// that applies is the closing board of the day the price belongs to — not today's, and not
        /// a mid-market quote. Business days only. Applies to US STOCK holdings.
        /// </summary>
        public const string Cathay = "CATHAY";
    }
}
