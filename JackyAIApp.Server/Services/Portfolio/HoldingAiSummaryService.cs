using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.OpenAI;
using System.Text;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>The AI technical summary (朱家泓觀點) of one held position.</summary>
    public interface IHoldingAiSummaryService
    {
        Task<ServiceResult<PortfolioAiSummaryDto>> GetAiTechSummaryAsync(string userId, string market, string symbol, CancellationToken ct = default);
    }

    public class HoldingAiSummaryService(
        IHoldingSnapshotReader snapshots,
        IPortfolioPriceLoader prices,
        IFundDataService fundData,
        IOpenAIPromptService openAI) : IHoldingAiSummaryService
    {
        public async Task<ServiceResult<PortfolioAiSummaryDto>> GetAiTechSummaryAsync(
            string userId, string market, string symbol, CancellationToken ct = default)
        {
            var pos = await snapshots.FindLatestPositionAsync(userId, market, symbol, ct);
            if (pos == null) return ServiceResult.Fail(ErrorCodes.NotFound, "找不到此持倉");
            var sym = symbol.ToUpperInvariant();
            var isFund = market.ToUpperInvariant() == "FUND";

            List<decimal> closes;
            List<decimal>? highs = null, lows = null;
            List<long>? vols = null;
            string currency;

            if (isFund)
            {
                var nav = await fundData.GetFundNavHistoryAsync(pos.Symbol, ct);
                if (nav == null || nav.Prices.Count == 0) return ServiceResult.Fail(ErrorCodes.NotFound, "無歷史價格資料");
                closes = nav.Prices.Select(p => p.Close).ToList();
                currency = nav.Currency;
            }
            else
            {
                // Technical indicators (MAs, S/R) need real history — deep fetch.
                var data = await prices.FetchStockAsync(symbol, deep: true, ct);
                if (data == null || data.HistoricalPrices.Count == 0) return ServiceResult.Fail(ErrorCodes.NotFound, "無歷史價格資料");
                var hp = data.HistoricalPrices; // ascending by date
                closes = hp.Select(p => p.Close).ToList();
                highs = hp.Select(p => p.High).ToList();
                lows = hp.Select(p => p.Low).ToList();
                vols = hp.Select(p => p.Volume).ToList();
                currency = pos.Currency;
            }

            var context = BuildTaContext(pos.Name, sym, isFund ? "基金" : pos.Market, currency, closes, highs, lows, vols);
            var userMessage = $"請以朱家泓技術分析觀點，總結「{sym} {pos.Name}」。以下為技術數據：\n\n{context}";

            var (summaryResult, summaryError) = await openAI.GetCompletionAsync<PortfolioAiSummaryDto>(
                "Prompt/Portfolio/ZhuTechAnalysisSystem.txt", userMessage, NewModels.GPT_6_LUNA);
            return summaryResult is null
                ? ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, summaryError ?? "AI 總結失敗")
                : ServiceResult.Ok(summaryResult);
        }

        /// <summary>Builds a compact technical-indicator brief (MAs, alignment, S/R, volume) for the LLM.</summary>
        private static string BuildTaContext(string name, string symbol, string market, string currency,
            List<decimal> closes, List<decimal>? highs, List<decimal>? lows, List<long>? vols)
        {
            decimal Ma(int n) => closes.Count >= n ? Math.Round(closes.TakeLast(n).Average(), 2) : 0m;
            var last = Math.Round(closes[^1], 2);
            var sb = new StringBuilder();
            sb.AppendLine($"標的：{symbol} {name}（{market}，計價 {currency}）");
            sb.AppendLine($"資料筆數：{closes.Count} 個交易日（由舊至新）");
            sb.AppendLine($"最新收盤/淨值：{last}");

            decimal ma5 = Ma(5), ma10 = Ma(10), ma20 = Ma(20), ma60 = Ma(60);
            sb.AppendLine($"5日均：{ma5}；10日均：{ma10}；20日均(月線/生命線)：{ma20}；60日均(季線)：{ma60}");

            var arrange = "資料不足";
            if (ma5 > 0 && ma10 > 0 && ma20 > 0 && ma60 > 0)
                arrange = ma5 > ma10 && ma10 > ma20 && ma20 > ma60 ? "均線多頭排列 (5>10>20>60)"
                        : ma5 < ma10 && ma10 < ma20 && ma20 < ma60 ? "均線空頭排列 (5<10<20<60)"
                        : "均線糾結/交錯（盤整）";
            sb.AppendLine($"均線排列：{arrange}");
            if (ma20 > 0)
                sb.AppendLine($"股價相對月線：{(last >= ma20 ? "站上" : "跌破")}月線，乖離 {Math.Round((last - ma20) / ma20 * 100, 1)}%");

            var win = Math.Min(20, closes.Count);
            var recent = closes.TakeLast(win).ToList();
            var hi20 = Math.Round(highs != null ? highs.TakeLast(win).Max() : recent.Max(), 2);
            var lo20 = Math.Round(lows != null ? lows.TakeLast(win).Min() : recent.Min(), 2);
            sb.AppendLine($"近{win}日高點(壓力)：{hi20}；近{win}日低點(支撐)：{lo20}");

            var win60 = Math.Min(60, closes.Count);
            var hi60 = Math.Round((highs ?? closes).TakeLast(win60).Max(), 2);
            var lo60 = Math.Round((lows ?? closes).TakeLast(win60).Min(), 2);
            sb.AppendLine($"近{win60}日高：{hi60}；近{win60}日低：{lo60}");

            sb.AppendLine($"最近5筆收盤：{string.Join(", ", closes.TakeLast(5).Select(x => Math.Round(x, 2)))}");

            if (vols != null && vols.Count >= 6)
            {
                var v5 = vols.TakeLast(5).Average();
                var vPrev = vols.Count >= 10 ? vols.SkipLast(5).TakeLast(5).Average() : v5;
                sb.AppendLine($"近5日均量：{(long)v5}；前5日均量：{(long)vPrev}（{(v5 >= vPrev ? "量增" : "量縮")}）");
            }

            return sb.ToString();
        }
    }
}
