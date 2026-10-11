# TASK-047：回落煞車卡片 — 台股正 2 類別（00631L＋00685L）與 NTSD 納入策略範圍（Boss #431 2026-10-11 20:09）

## Owner
Frontend Engineer

## Status
READY

## Priority
HIGH（Boss 正在正式環境試用；NTSD 對帳行讓卡片曝險與右上角水位對不上，是 Boss 最初就要消除的落差）

## Objective
依 Boss 在 #431 的留言：(1) **00685L 與 00631L 同性質，歸為一類、一起算加碼／減碼／不動**；(2) **水位要含 NTSD**——NTSD 納入策略範圍、計入整體曝險，卡片 E 與右上角水位同一個數字，不再有「不含 NTSD」的對帳行。純前端，後端與 API 契約不動。

產品規格：`REQUIREMENTS.md` 需求 12 **v4.3**、`DECISIONS.md` **DECISION-021**（取代 DECISION-018；PM 假設 A27～A29：NTSD 計入但不給建議、股數依兩檔市值比例分攤、00685L 倍數 2）。設計全文：`ARCHITECTURE.md`「回落煞車 — 台股正 2 類別（00631L＋00685L）與 NTSD 納入範圍」。**Engineer 不改 `REQUIREMENTS.md`／`DECISIONS.md`**。

## Requirements
1. **NTSD 納入範圍**
   - `drawdownBrakeScope.ts` 的 `STRATEGY_EXCLUDED` 改為空表 `[]`；機制（`isExcluded`、`excluded`／`excludedValue`、對帳行 `describeReconciliation`）**保留**，只是目前沒有任何標的被排除，對帳行因此不顯示。
   - 為了讓「對帳行」仍可測試：`classifyPositions` 增加第二個參數 `excluded = STRATEGY_EXCLUDED`（預設值），測試以假的排除表驗證對帳行；不要因為表是空的就刪掉機制。
   - NTSD 落入 `otherMulti`（倍數 1.5）、計入 `A` 與 `E`，**仍不給建議**（DECISION-020）。畫面上「其他倍數商品」列沿用。
   - 既有以 NTSD 為例的測試（`drawdownBrakeScope.test.ts`、`DrawdownBrakeCard.test.tsx`、`drawdownBrake.test.ts`、`drawdownBrakeCopy.test.ts`）依新行為改寫：NTSD 計入 E／A；**沒有被排除持股時卡片 E ＝ `exposureOfLiquidAssetsPct`（水位）**，對帳測試用含 NTSD 的持股組合重做一次。
2. **台股正 2 類別 `lev2`**
   - `drawdownBrakeScope.ts` 新增單一成員表 `CORE_LEVERAGED_2X = ['00631L', '00685L']`（市場 `TW`、`assetType` `STOCK`）。**不要**寫成「TW 且倍數 2 就歸類」。
   - `ScopeClassification.core00631L` → `coreLev2`（`BucketTotals`，合計兩檔）；`ScopeHoldings['00631L']`、`CoreTargets['00631L']`、`Weights['00631L']` 改名 `lev2`。`advise` 的算法**不變**（`lev2` 取代原本 `00631L` 的位置，倍數 2）。
   - 新增純函式 `splitLev2Order`（放 `drawdownBrake.ts` 或 `drawdownBrakeScope.ts`），把類別的金額變動分配到各檔，規則見設計文件：減碼依各檔現值比例、每檔上限＝持有股數；加碼依持有現值比例；都沒持有 → 全部 `00631L`；只持有一檔 → 全給那一檔；`roundHalfAwayFromZero`。`Advice.orders` 仍是「各檔股數」的清單（先賣後買），另加類別層級的金額供 UI 顯示。`cashChange` 用實際股數。
   - 分桶表（`BucketList`）：原「00631L」列改為「台股正 2（00631L／00685L）」，其下縮排列各檔現值與建議股數（只列有持有或有建議的）。
   - `CORE_FACTORS` 倍數檢查涵蓋兩檔（`exposureFactor` 必須是 2，否則 `coreFactorAnomaly`）；`extractStrategyPositions`、`DrawdownBrakeRefresh` 的 `heldSymbols`、`useStrategyPrice` 由二元 `StrategySymbol` 改為「核心標的清單」（0050 ＋ `CORE_LEVERAGED_2X`）：持有才重抓報價與查價；沒持有時只有 `00631L`（預設買進標的）需要查價。
   - **`SPLIT_EVENTS` 不新增 00685L 條目**，除非 PM／Boss 提供日期與比例（Boss 待答，非阻擋）。不得自行猜測。在表旁留一行註解說明「00685L 分割史待 Boss 確認」。型別要允許核心標的沒有條目（現為 `Record<StrategySymbol, …>`，改為 `Partial<Record<…>>` 或 `Record<string, SplitEvent>`），`findSplitGuardHit` 對沒有條目的標的不擋。
3. **文案**（`drawdownBrakeCopy.ts`；PM 可改字，Engineer 先用以下提議）
   - `BACKTEST_SCOPE_NOTE`：「以上績效依 0050＋00631L＋現金回測；00685L 視同 00631L（您的判斷），未單獨回測，也不是整個流動資產的績效。」（PM v4.3 用語；PM 可再改字）
   - `aboveTarget` 文案「即使 0050／00631L 全部轉為現金」→「即使 0050 與台股正 2 全部轉為現金」。
   - 其餘出現「0050／00631L」字樣之處（目標配置字串 `formatTargetMix`、`DrawdownBrakeRefresh` 註解、`coreFactorAnomaly` 提示）改為涵蓋正 2 類的說法；`formatTargetMix` 顯示 `台股正 2 60%`（不逐檔列目標，目標只有類別層級）。
4. **（Optional，可不做，做了請在 Result 說明）重新整理涵蓋範圍內價格失敗的持股**：TASK-043 的「重新整理資料」目前只對持有的核心標的呼叫 `refresh-price`。需求 12 之後，**範圍內任何持股（含 NTSD）價格失敗就暫停整張卡片的建議**，使用者唯一的手段是重新載入頁面。建議：重新整理時，除核心標的外，再對 `scope.priceFailed` 的持股呼叫 `refresh-price`（上限 5 檔，沿用現有 30 秒客戶端冷卻）。
5. **不在範圍**：後端與 API；其他正 2 ETF（例如 00675L）自動納入；NTSD 的加減碼建議；`REQUIREMENTS.md`／`DECISIONS.md`；TASK-038。

## Acceptance Criteria
- **退化對照**：只持有 0050／00631L／現金、沒有其他持股時，`advise` 與卡片輸出與合併前**逐項相同**（既有 `advise`／`composition` 測試期望值一律不改，只換輸入型別／鍵名）。
- **Golden（`ARCHITECTURE.md` S4～S6；平衡等級，價格 0050＝60、00631L＝30、00685L＝20）**：
  - S4（0050 0.5M／00631L 3.0M／00685L 2.0M／現金 1.0M，OFFENSE，未切換）→ 減碼：00631L 賣 22,000 股、00685L 賣 22,000 股、0050 買 13,333 股；`cashChange` ＝ +300,020；執行後再算＝「不動」。
  - S5（0050 1.0M／現金 5.0M）→ 加碼：買 00631L 120,000 股、0050 3,333 股；`cashChange` ＝ −3,799,980；執行後＝「不動」。
  - S6（0050 0.5M／00685L 5.0M／現金 1.0M）→ 減碼：00685L 賣 55,000 股、0050 買 13,333 股。
  - **S7 改版前後對照**（Boss 持有 NTSD 與 00685L；0050 1.0M／00631L 1.5M／00685L 1.0M／現金類 2.0M／其他一倍 1.0M／NTSD 1.0M；價格同上）：**後**（本任務）→ A＝7.5M、`eActual`＝1.1333（＝水位 1.1333，差異僅四捨五入）、目標 0050 0／正 2 類 4.0M／現金 1.5M、`eReach`＝1.40、`capped`＝false；建議「加碼」：0050 賣 16,667 股、00631L 買 30,000 股、00685L 買 30,000 股；`cashChange` ＝ −499,980；無對帳行；NTSD 在「其他倍數商品」列、不動。**前**（合併前行為，供對照，不需測）→ A＝6.5M、E＝1.0769、建議賣 0050 11,667 股、買 00631L 46,667 股。執行後再算＝「不動」。
  - 分配規則另有單元測試：減碼上限（不賣超過持股）、取整、只持有一檔、都沒持有（預設 00631L）、加碼只給持有的比例。
- **NTSD**：含 NTSD 的持股組合 → 卡片 E ＝ 水位（差異僅四捨五入）；`scope.excluded` 為空、不顯示對帳行；NTSD 計入「其他倍數商品」列、沒有建議欄位；NTSD 價格失敗 → 不給建議並列出 NTSD。
- 倍數異常防護：00685L 的 `exposureFactor` 不是 2 → 不給建議。
- 分割防護：未新增 00685L 條目；既有 0050／00631L 防護行為不變（既有測試保留）。
- `npm run lint`（0 warnings）、`prettier --check`、`npm run test`、`npm run build` 全過；360／400／1280px 版面無橫向捲動（附截圖放 `.agent/tasks/TASK-047/`，金額須標示為示意資料）。
- 不新增依賴；後端、`DrawdownBrakeDto`、`portfolioApis` 無變更。

## Technical Notes
- 設計取捨與三個情境、為何用成員表而非通則，見 `ARCHITECTURE.md`。「買哪一檔」是 `splitLev2Order` 唯一決定的地方，Boss 日後偏好只改它。
- `exposureFactor` 由後端算好：00685L 名稱「群益臺灣加權正2」含「正2」且代碼尾碼 `L`，兩條規則都回 2；不要在前端另寫倍數規則。
- 目標 E、k、參數、觸發規則（DECISION-013）一律不動。
- 沙箱連不到證交所／Yahoo；以單元測試與 GitHub Actions 為準。

## Dependencies
TASK-046（#437）、TASK-043（#442）皆已 MERGED。無其他相依。工作流程：開工前先把 `Status: IN_PROGRESS` 推到 master（`ARCHITECTURE.md`「Workflow conventions」）。

## PR
（待填）

## Result
（待填）

## Review
（待填）
