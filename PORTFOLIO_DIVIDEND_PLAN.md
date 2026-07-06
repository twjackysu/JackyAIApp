# Portfolio 含息報酬(Total Return)— 架構設計

> 目標:Portfolio 同時顯示「不含息報酬」與「含息報酬」(價格報酬 vs 總報酬)。
> 資料源已全部實測驗證(2026-07-04),API 細節與 response 形狀在第二節。
> 供 Sonnet 實作。設計原則:效能、可維護、可擴充、可讀。

## 一、產品決策

**不用 checkbox,兩個報酬並排顯示。** 理由:含息/不含息對比本身就是資訊(現金流 vs 價差),toggle 會藏掉一半;版面上只是多一行。呈現:
- Summary 卡:現有「未實現損益」行下方加「含息損益」行 = 未實現損益 + 累計已領股息,附「其中股息 NT$X」caption。
- 持倉表:損益欄下方以 caption 顯示「+股息 NT$X」(有股息的持倉才顯示)。

## 二、資料源(已實測,可直接照抄)

### 台股 + 美股:Yahoo Finance v8 Chart API(同一支,已在用)
現有 `YahooFinanceMarketDataProvider` 就是打這支 API。配息只要 **加一個 query 參數**:

```
GET https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?range=5y&interval=1mo&events=div
Header: User-Agent: Mozilla/5.0
```
- symbol:美股原樣(`AAPL`);台股上市加 `.TW`(`2330.TW`、`0056.TW`);上櫃加 `.TWO`(`6488.TWO`)。全部實測 OK。
- Response:`chart.result[0].events.dividends` = `{ "<unix_ts>": { "amount": 每股金額, "date": unix_ts } }`,date = **除息日**。`chart.result[0].meta.currency` 給幣別(TW=TWD、US=USD)。
- 實測樣本:AAPL 季配 0.26/0.27 USD;2330.TW 季配 4.0→6.0 TWD;0056.TW、00878.TW、6488.TWO 全對。
- 注意:`interval=1mo` 即可(只要 events,不用日線);range 用 `5y` 涵蓋所有可能的 snapshot 歷史。

### 基金:FundRich 私有 API(與現有 NAV 來源同站)
```
POST https://apis.fundrich.com.tw/FRSDataCenter/GetFundDividendHistory
Body: {"data":{"fundId":"076009"}}
Headers: 同現有 FundDataService.AddBrowserHeaders(缺 Origin/Referer 會 406)
```
- Response:`data.tableRow.history[]` = `{ baseDay: "2026/03/10"(發放日), exDividendDate: "2026/03/06"(除息日), perUnitPrice: "5.9100"(每單位金額), internalRateOfDividend: "2.9529%" }`,完整多年歷史,新到舊。
- 幣別:history 內**沒有**幣別欄位 → 用現有 `GetFundNavHistoryAsync` 回傳的 `FundNavHistory.Currency`(同一檔基金同幣別)。
- 累積型基金(不配息)回傳空 history 或 `GetFundDividendInfo.data.dividend=false` — 空陣列自然處理,不用特判。
- 防禦模式照抄 FundDataService 現有寫法:任何失敗 → 空結果 + LogWarning,不 throw。

## 三、計算設計(核心:snapshot 時間軸 × 除息日)

App 沒有交易紀錄,但有 **snapshot 時間軸** → 任一天的持有數量可查(生效日 ≤ 該日的最新 snapshot)。這正好是股息計算需要的全部資訊:

```
某持倉累計股息 = Σ over 每筆除息日 d(第一個 snapshot 日 ≤ d ≤ 今天):
                  當日持有數量(from snapshot timeline) × 每股配息金額(d)
換算 TWD:乘該資產幣別匯率(沿用現有 fx 邏輯)
```

- 正確處理加碼/減碼:除息日當下持有多少就算多少。
- 起算點 = 使用者第一個 snapshot 的 EffectiveDate — 之前領的股息 app 不知道持有狀況,不算(誠實原則,同 XIRR 的處理哲學)。
- 實作位置:`PortfolioService` 新 private method `ComputeDividendsAsync`,輸入 snapshots(`GetNetWorthHistoryAsync` 已有同樣的載入邏輯可參考)+ dividend histories,輸出 per-symbol 累計股息 TWD。

## 四、後端變更點

### 新檔 `Services/Portfolio/IDividendService.cs` + `DividendService.cs`
獨立 service(不塞進 FundDataService/YahooProvider,職責分離):
```csharp
public record DividendRecord(DateOnly ExDate, decimal AmountPerUnit, string Currency);

public interface IDividendService
{
    /// <summary>股票配息(台股/美股),Yahoo events=div。symbol 不含 suffix,內部依 market 加 .TW/.TWO。</summary>
    Task<List<DividendRecord>> GetStockDividendsAsync(string symbol, string market, CancellationToken ct);
    /// <summary>基金配息,FundRich GetFundDividendHistory。幣別由呼叫端用 NAV currency 補。</summary>
    Task<List<DividendRecord>> GetFundDividendsAsync(string fundId, CancellationToken ct);
}
```
- 台股 suffix 判斷:先試 `.TW`,404/空 再試 `.TWO`(或依現有 TWStockLib 的上市/上櫃資訊,如果拿得到)。結果連同「哪個 suffix 成功」一起 cache。
- Cache:**24h TTL**(配息資料一天內不會變),`CacheTtl.Portfolio` 加 `Dividend` 常數。失敗 → 空 list + LogWarning,絕不 throw。
- 全部持倉的 dividend fetch 用 `Task.WhenAll` 平行(照抄 `FetchStocksAsync` 模式)。

### `PortfolioService.GetPortfolioSummaryAsync`
1. 載入全部 snapshots(現有 `GetNetWorthHistoryAsync` 已有此 query,抽成共用 private method)。
2. 對每個 distinct stock/fund symbol 平行抓 dividends(cache 命中時零成本)。
3. `ComputeDividendsAsync`:依第三節公式算 per-symbol 累計股息 TWD。
4. DTO 加欄位。

### `DTO/Portfolio/PortfolioDto.cs`
```csharp
// PositionDto 加:
public decimal DividendsTWD { get; set; }          // 持有期間累計已領股息(0 = 無)
// PortfolioSummaryDto 加:
public decimal TotalDividendsTWD { get; set; }      // Σ positions.DividendsTWD
public decimal TotalReturnPnlTWD { get; set; }      // UnrealizedPnlTWD + TotalDividendsTWD
public decimal TotalReturnPct { get; set; }         // TotalReturnPnlTWD / CostBasisTWD × 100(cost>0 才算)
```
不含息欄位全部不動 — 純加法。

### XIRR 含息(做,但獨立 commit)
`ComputeAnnualizedReturnAsync` 的 flows 加入股息 inflow:`flows.Add((發放日, +divTWD))`。
- 現有 XIRR 變成「含息年化」— 這其實修正了現況低估(股息是真實報酬)。
- UI 標籤改「年化報酬率 (XIRR·含息·估)」。

### 測試(`PortfolioDividendTests.cs`)
1. 除息日在持有期間 × 數量 → 股息正確累計(mock IDividendService)。
2. 除息日早於第一個 snapshot → 不計。
3. 中途加碼:兩個 snapshot 數量不同,兩筆除息日各用當時數量。
4. USD 股息 × fx 換算。
5. 無配息持倉 DividendsTWD = 0,summary 不含息欄位與現況完全相同(回歸)。
6. DividendService 解析:Yahoo JSON 樣本 + FundRich JSON 樣本(存 fixture 字串,不打真網路)。

## 五、前端變更點

- `apis/portfolioApis/types.ts`:PositionDto 加 `dividendsTWD`,Summary 加三欄位。
- `Portfolio/index.tsx` NetWorthCard:未實現損益行下方加「含息損益」行(▲/▼ + %,樣式照抄現有 pnl 行),caption「含累計股息 NT$X」。隱私遮罩(`change()`/`value()`)必須套。
- `Portfolio/PositionsTable.tsx`:損益欄有 `dividendsTWD > 0` 時加 caption「+息 NT$X」。
- `portfolioExport.ts`:CSV 加「累計股息(TWD)」「含息損益(TWD)」欄。

## 六、效能
- 每個 distinct symbol 多 1 次外部 call,**24h cache** 後攤提到幾乎零;全部平行。
- Yahoo `interval=1mo` 減少 payload(只要 events)。
- 不動現有價格抓取路徑 — dividends 完全獨立,失敗不影響現有 summary(股息欄位顯示 0)。

## 七、非目標(明確不做)
- **台股股票股利(配股)**:Yahoo events=div 只有現金股利。配股會改變持股數,snapshot 模型下使用者自己更新數量即可。文件註明此限制。
- 股息再投入(DRIP)模擬、稅務(二代健保補充保費、美股 30% withholding)— 顯示的是**稅前**股息,UI caption 註明「稅前」。
- 已實現損益/歷史配息明細頁(之後可做,DividendRecord 結構已支援)。

## 八、驗收
1. 持有 0056(有配息)時:summary 出現含息損益行,金額 = 未實現損益 + 除息日×數量的累計,持倉表該列顯示「+息」caption。
2. 純現金/負債 portfolio:所有新欄位為 0,畫面與現況無異(回歸)。
3. 美股股息以 USD×fx 換算 TWD。
4. 基金(月配/年配型)股息正確;累積型基金為 0。
5. 隱私模式下股息金額被遮罩。
6. CSV 含新欄位。
7. `dotnet test`(含新測試)、`npm run lint`、`tsc --noEmit` 全綠;Server.Tests 專案要 build。
8. Yahoo/FundRich 掛掉時:summary 照常回傳,股息欄位 0,log warning — 絕不 500。
