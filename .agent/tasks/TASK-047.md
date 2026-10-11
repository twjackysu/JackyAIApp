# TASK-047：回落煞車卡片 — 台股正 2 類別（00631L＋00685L）與 NTSD 納入策略範圍（Boss #431 2026-10-11 20:09）

## Owner
Frontend Engineer

## Status
MERGED（PR #444，Boss 於 2026-10-11 合併；Build and deploy run 532 成功。Tech Lead 事後審閱見 Review）

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
   - **`SPLIT_EVENTS` 不新增 00685L 條目**：Boss 於 2026-10-11 確認 00685L 沒有做過分割（上市未久）；Tech Lead 無法獨立驗證，以 Boss 的確認為準。在表旁留一行註解「00685L：Boss 2026-10-11 確認無分割；日後若分割，在此加一行」。不得自行猜測日期。型別要允許核心標的沒有條目（現為 `Record<StrategySymbol, …>`，改為 `Partial<Record<…>>` 或 `Record<string, SplitEvent>`），`findSplitGuardHit` 對沒有條目的標的不擋。
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
https://github.com/twjackysu/JackyAIApp/pull/444（`claude/nice-allen-smkjdp` → `master`）

## Result
- 實作（全在 `jackyaiapp.client/src/Portfolio/`，後端、`DrawdownBrakeDto`、`portfolioApis` 無變更、無新依賴）：
  - `drawdownBrakeScope.ts`：`STRATEGY_EXCLUDED = []`（機制保留，`classifyPositions`／`isExcluded` 新增 `excluded` 參數）；新增 `CORE_LEVERAGED_2X`、`Lev2Symbol`、`CORE_SYMBOLS`；`core00631L` → `coreLev2`，另有 `lev2Holdings`（各檔現值、股數）；`CORE_FACTORS` 由成員表產生。
  - `drawdownBrake.ts`：`lev2` 取代 `'00631L'` 鍵（`ScopeHoldings`／`CoreTargets`／`Weights`）；`advise` 數學不變；新增 `splitLev2Order`、`DEFAULT_LEV2_BUY`、`Lev2Holding`、`StrategyPrices`（Partial）；`Advice.lev2Change`。
  - `drawdownBrakeHoldings.ts`：`SPLIT_EVENTS` 改 `Partial`，未新增 00685L（旁註 Boss 2026-10-11 確認）；`requiredPriceSymbols`、`priceRefreshSymbols`。
  - `drawdownBrakeCopy.ts`／`DrawdownBrakeCard.tsx`／`DrawdownBrakeRefresh.tsx`：文案、正 2 類別列與各檔縮排明細、三檔查價 hooks、重新整理範圍（`heldSymbols` → `refreshSymbols`）。
- **Optional 第 4 點有做**：重新整理一併對 `scope.priceFailed` 中的**股票**呼叫 `refresh-price`（基金不列——後端對非 STOCK 是靜默 no-op；同代號一次；失敗者上限 5 檔；沿用 30 秒客戶端冷卻）。
- 驗證：`npm run lint`（0 warnings）、`prettier --check`、`npm run test`（55 檔 650 項）、`npm run build` 全過；既有 `advise`／`composition` 期望值未改（只換鍵名）；S4～S7 golden 與執行後「不動」皆有測試；20 處變異測試皆被抓到；Playwright＋Chromium 對真實卡片（API 替身、harness 不提交）5 情境 × 1280／400／360px、展開前後 `scrollWidth == innerWidth`、無 console error（僅 harness 缺 favicon 的 404）。截圖（**示意資料**）：`TASK-047/desktop-1280-s4-two-lev2.png`、`mobile-360-s7-ntsd-00685L.png`、`mobile-400-s7-ntsd-00685L.png`、`mobile-360-s6-only-00685L.png`、`mobile-360-ntsd-price-failed.png`。
- 與任務書字面不同／自行選擇：
  - `ScopeHoldings.lev2Holdings` 為選填：省略時視為正 2 類全部持有在 00631L（股數＝市值÷價格），只為了讓既有 `advise` 測試只換鍵名、期望值不動；`toScopeHoldings` 一律提供。
  - `Advice.lev2Change`（類別層級金額）＝Σ(實際建議股數×價格)，與下方各檔「約 NT$」合計一致，不是理想值。
  - 類別列建議欄文案為「加碼：買進約 NT$X／減碼：賣出約 NT$X／不動」，各檔列沿用既有 `describeCoreRowAdvice`。
  - 分桶表為了縮排明細用了巢狀 `<ul>`（`aria-label`「台股正 2（00631L／00685L）各檔」）；測試的 `renderRows` 因此改成依 `<ul>` 配對取範圍。
  - 查價只在用得到時進行：只持有 00685L 時 00631L 不查價、也不擋建議。
  - `priceRefreshSymbols` 放在 `drawdownBrakeHoldings.ts`（避免 `drawdownBrakeRefresh.ts` 與 `DrawdownBrakeRefresh.tsx` 兩檔只差大小寫在卡片同時被 import 的風險）。
- 已知限制：持有三檔核心標的＋最多 5 檔失敗股票時，一次按下最多 8 個 `refresh-price`（各回整份 summary；有 30 秒客戶端冷卻）；與真實後端回應的整合未在沙箱驗證（連不到證交所／Yahoo），請 Boss 在正式環境目視確認。
- 流程：本 session 僅限推 feature branch，未能先把 `IN_PROGRESS` 推到 master（同 TASK-043）；未動 `PROJECT_STATUS.md`（避免檔尾衝突，交 Tech Lead 於審查時一併記錄）。

## Review
**Tech Lead 事後審閱（2026-10-11，排程執行第 4 次；#444 在審閱前已被 Boss 合併並部署，當時未走審閱）**
- 我讀了 `splitLev2Order`（買／賣哪一檔的唯一決定處）與 `SPLIT_EVENTS`，並對照 `ARCHITECTURE.md` 的 S4～S7：測試中的 golden 數字與設計逐項一致（S4 賣 00631L／00685L 各 22,000、買 0050 13,333；S5 買 00631L 120,000＋0050 3,333；S6 賣 00685L 55,000＋買 0050 13,333；S7 賣 0050 16,667、買 00631L／00685L 各 30,000）。其餘（`drawdownBrakeScope.ts`、卡片 UI、文案、重新整理）依 Result、CI（run 651／652 成功）與部署核對，**未逐行讀**。
- `splitLev2Order` 行為核對：減碼依各檔現值比例且每檔不超過持有股數；加碼依持有現值比例；都沒持有買 00631L（PM A28）；價格無效的標的略過、不丟例外（上游在價格失敗時已暫停整張卡片的建議，所以不會用到不完整的分配）。
- `SPLIT_EVENTS` 改為 `Partial<Record<…>>` 且不新增 00685L：符合 Boss 2026-10-11 的確認（上市未久、無分割）；`findSplitGuardHit` 對無條目的標的不擋，行為正確。日後若分割，在表中加一行。
- Engineer 的自選項（`lev2Holdings` 選填且省略時視為全持有在 00631L；`Advice.lev2Change` ＝ Σ(實際建議股數×價格)；巢狀 `<ul>` 縮排明細；只在用得到時查價；`priceRefreshSymbols` 放在 `drawdownBrakeHoldings.ts`）皆**可接受**。Optional 項（重新整理一併重抓價格失敗的股票，上限 5 檔、基金不列）**接受**。
- 已知限制（一次按下最多 8 個 `refresh-price`、各回整份 summary，有 30 秒客戶端冷卻）**接受**；日後若要更省，可由後端提供批次端點（Future Work，不排）。
- 結論：無須追加修正。**仍需 Boss 在正式環境目視確認**：持有 NTSD、00685L 時，卡片 E 與右上角水位是同一個數字（S7）；NTSD 價格抓失敗時整張卡片暫停建議並列出 NTSD，按「重新整理資料」可重抓。
