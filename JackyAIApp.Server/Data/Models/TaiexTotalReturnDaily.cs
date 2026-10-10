using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// One finalized daily close of TWSE's 發行量加權股價報酬指數 (the dividend-inclusive TAIEX),
    /// the series the 回落煞車 signal is computed from. Shared across all users. Like
    /// <see cref="DailyStockPrice"/> it only ever holds days that have already closed: today's
    /// still-moving intraday estimate must never be written here, because the signal is rebuilt
    /// from this table and an unfinished value would be baked into every later day's high and
    /// average. Kept apart from <see cref="DailyStockPrice"/> so an index never mixes into the
    /// per-stock table.
    /// </summary>
    public class TaiexTotalReturnDaily
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public DateOnly TradeDate { get; set; }

        public decimal Close { get; set; }
    }
}
