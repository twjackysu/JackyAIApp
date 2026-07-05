# Portfolio 負債(LIABILITY)功能 — 架構設計

> 目標:Portfolio 可填寫負債(信貸),淨值與曝險計算反映真實槓桿。
> 範圍刻意排除:房貸、不動產、利率、攤還排程(見「非目標」)。
> 供 Sonnet 實作。設計原則:效能、可維護、可擴充、可讀。撰寫日期:2026-07-04。

## 一、核心設計決策(先讀懂再動手)

### 決策 1:新增 AssetType.LIABILITY,不是負數現金
負數現金會汙染所有假設金額為正的地方(占比、群組小計、圖表、隱私遮罩)。獨立類別讓每個touchpoint 顯式處理,語意清楚。

### 決策 2:儲存正數,估值時一次性轉負(唯一符號轉換點)
- DB `HoldingPosition.Quantity` 存**正數**(欠款金額)— 使用者輸入直覺、驗證規則(`Quantity > 0`)不用改。
- `PositionDto.MarketValueTWD` 對 LIABILITY 回傳**負數** — 符號在 `PortfolioService` 估值迴圈裡轉,**全 codebase 只有這一個地方碰符號**。
- 效益:下游所有「Σ market value」的邏輯(淨值、日變化、歷史、匯出總計)**自動正確,零修改**。這是本設計可維護性的核心——不要在多處各自判斷 LIABILITY 再減。

**不變式(寫進程式碼註解)**:
```
DB Quantity:      永遠 > 0(欠款金額)
MarketValueTWD:   LIABILITY 為負,其他為正
TotalNetWorthTWD: 永遠 = Σ positions.MarketValueTWD(無特例)
```

### 決策 3:曝險公式不動,分母自動變準
`ExposureRatioPct = equityExposureTWD / netWorthTWD`。LIABILITY 的 `exposureFactor = 0`(分子不變),淨值因負值變小(分母變小)→ 曝險%上升,正確反映槓桿。借錢投資時 >100% 是 feature 不是 bug,前端不要 clamp。

### 決策 4:占比(weight%)對負債顯示「—」
負值占比無意義。權重分母改用「正值部位總和」(= 總資產,不是淨值),負債列該欄顯示「—」。

## 二、資料層(零 migration)

`Data/Models/Portfolio.cs`:
```csharp
public enum AssetType { STOCK, CASH, FUND, LIABILITY }   // 只准 append,int 已入庫,禁止重排
```
- AssetType 以 int 存(已確認 migration 無 CHECK constraint)→ **不需要 EF migration**,不碰 production DB schema。
- `HoldingPosition` 欄位重用,不加欄位:

| 欄位 | LIABILITY 用法 |
|---|---|
| Symbol | 空字串(同 CASH) |
| Name | 使用者自訂,預設「信貸」 |
| Market | 空字串 |
| Currency | "TWD"(允許 "USD",處理邏輯與 CASH 相同) |
| Quantity | 欠款金額,正數 |
| UnitPrice / CostBasis / CostCurrency | null(LIABILITY 無成本概念,不參與損益/XIRR) |

## 三、後端變更點(全部列出,不要有遺漏或多做)

### `Services/Portfolio/PortfolioService.cs`
1. **SaveHoldingsAsync** 過濾條件:現有 `p.Quantity > 0 && (CASH || has symbol)` → LIABILITY 加入免 symbol 行列(與 CASH 同組)。CostCurrency 對 LIABILITY 一律存 null。
2. **估值迴圈**(`GetSummaryAsync` 內):LIABILITY 分支 — `currentPrice = 1`、`valueLocal = -Quantity`(**符號轉換唯一點**)、`priceFailed = false`、不進損益計算(CostBasis 為 null 自然跳過,不用加 if)。
3. **分類累加** switch 加 `case LIABILITY: liability += valueTWD;`(累出來是負數)。
4. **ValueAsOfDate**(日變化用)與 **GetNetWorthHistoryAsync** 的估值邏輯:同樣加 LIABILITY 分支(value = -Quantity × fx)。價格恆為 1 → 不會產生假日變化,也不觸發 `HasNewerPriceThan`,行為正確。
5. **XIRR(ComputeAnnualizedReturnAsync)**:LIABILITY 無 CostBasis → 不進 flows;terminal value 用的是 costBasis+pnl,不含負債 → **零修改**,但要加測試釘住此行為(信貸不是「投入成本」,不應扭曲報酬率)。

### `Services/Portfolio/PortfolioExposure.cs`
`Factor()` 開頭加 `if (type == AssetType.LIABILITY) return 0m;`。

### `DTO/Portfolio/PortfolioDto.cs`
- `PortfolioSummaryDto` 加 `decimal LiabilityTWD`(**正數**,欠款總額,顯示用;TotalNetWorthTWD 已是淨額)。
- `NetWorthDataPoint` 加 `decimal LiabilityTWD`(同樣正數)。
- 註解寫明正負號慣例。

### 測試(`Server.Tests`,新增檔案 `PortfolioLiabilityTests.cs`)
1. 存 100 萬現金 + 30 萬信貸 → NetWorth = 70 萬、LiabilityTWD = 30 萬。
2. 100 萬股票 + 50 萬現金 + 30 萬信貸 → ExposureRatioPct ≈ 83.3(驗證分母是淨值)。
3. LIABILITY 不影響 CostBasisTWD / UnrealizedPnl / XIRR。
4. 快照存檔:LIABILITY 無 symbol 不被過濾掉;Quantity ≤ 0 被過濾。
5. `PortfolioExposure.Factor(LIABILITY, ...) == 0`(加進現有 PortfolioExposureTests)。

## 四、前端變更點

### `apis/portfolioApis/types.ts`
- `AssetType` 加 `'LIABILITY'`;`PortfolioSummaryDto` / `NetWorthDataPoint` 加 `liabilityTWD`。

### `Portfolio/editorTypes.ts`
- `makeRow('LIABILITY')`:name 預設「信貸」、currency TWD、quantity 0、無 cost 欄位。
- `isRowEmpty` / `rowError`:與 CASH 同規則(金額必填 > 0)。

### `Portfolio/HoldingsEditor.tsx`
- 新 Section「負債」(現有 Section 元件重用):欄位 = 名稱 + 金額 + 幣別,放在現金區塊後面。UI 模式完全比照現金區塊,不發明新互動。

### `Portfolio/PositionsTable.tsx`
- `GROUP_ORDER` 加 `LIABILITY`(排最後),`GROUP_LABEL` = 負債,chip color = `error`。
- 金額顯示負數紅色(沿用現有 `pnlColor` 邏輯);占比欄顯示「—」;群組 header 的占比也「—」。
- 群組權重分母改為「正值部位總和」(見決策 4)— 注意現有 `total` 是全部加總,要改成 `positions.filter(v>0)` 的和,並確認既有資產群組百分比不因此改變(負債出現前兩者相等)。

### `Portfolio/index.tsx`(Summary 卡區)
- 顯示「負債 NT$-xxx」摘要卡或列(紅色),沿用隱私遮罩(`usePrivacy` 的 value mask 必須套用)。

### `Portfolio/NetWorthChart.tsx`
- 加「負債」series,值取 `-liabilityTWD` 進同一個 stack(ECharts 負值 area 畫在零軸下方,stack 頂端仍為淨值,tooltip 不用改邏輯)。顏色用 error 系。

### `Portfolio/portfolioExport.ts`
- `TYPE_LABEL` 加 LIABILITY=負債;金額輸出負數;淨值歷史 CSV 加 liability 欄。

## 五、效能要求
- 零新查詢、零新 API round-trip:LIABILITY 走既有 snapshot 載入路徑,不查價(price 恆 1),不進 stock/fund 的價格批次抓取清單(實作時確認 LIABILITY 不會被誤加進 `stockMap`/`fundMap` 的抓取 symbol 集合)。
- 零 schema migration、零資料回填。

## 六、可擴充性:未來新資產類別 checklist
本設計刻意讓「加一種資產類別」變成機械步驟(未來 REAL_ESTATE 直接照做):
1. enum append(後端 + 前端 types)
2. `PortfolioExposure.Factor` 加分支
3. `PortfolioService` 估值迴圈 + 分類累加 + ValueAsOfDate/History 加分支
4. SummaryDto/NetWorthDataPoint 加分類欄位
5. editorTypes `makeRow`/驗證 + HoldingsEditor Section
6. PositionsTable 群組 + NetWorthChart series + export label
把這份 checklist 留在 `Portfolio.cs` 的 enum 註解或本文件,下次照抄。

## 七、非目標(明確不做)
- 房貸/不動產:成對原則 — 只設房貸不設房產會讓淨值失真(使用者房貸僅 4 成,房產價值是貸款 2.5 倍);兩者一起設又會稀釋曝險%的投資監控意義。整組不做。
- 利率、攤還排程、剩餘期數:snapshot 模型記「當下欠多少」,還款 = 下次 snapshot 填新餘額。不引入時間序列計算。
- 負債的損益/報酬概念:無 CostBasis,永遠不進 P&L 與 XIRR。

## 八、驗收
1. 編輯器可新增「信貸 30 萬」,存檔後 summary:淨值減 30 萬、負債卡顯示 30 萬、曝險%上升。
2. 持倉表出現「負債」群組,金額紅色負數,占比「—」。
3. 淨值歷史圖出現零軸下方的負債帶,stack 頂 = 淨值。
4. 隱私模式下負債金額同樣被遮罩。
5. CSV 匯出含負債列(負數)。
6. `dotnet test`(含新測試)、`npm run lint`、`tsc --noEmit` 全綠;**Server.Tests 專案也要 build**。
7. 現有使用者(無負債 snapshot)所有數字與改動前完全相同(回歸保證)。
