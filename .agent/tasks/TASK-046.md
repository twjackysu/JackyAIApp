# TASK-046：回落煞車卡片 — 以整個流動資產判斷曝險、分桶顯示、核心目標算法（需求 12a／12b／12c，#431）

## Owner
Frontend Engineer

## Status
READY（2026-10-11 Tech Lead 建立；PM 於需求 12 v4.2 / DECISION-017·018·020 已決定產品規則，無待決問題）

## Priority
HIGH（Boss 在正式環境試用後直接提出：卡片曝險與他每天看的右上角「水位」對不上，用不下去）

## Objective
卡片的「目前有效曝險」改為整個流動資產（扣除被排除的 NTSD），與右上角水位同一個數字；以分桶表顯示 0050／00631L／其他一倍商品／（其他倍數商品）／現金類各自的目前值、目標、建議；**只對 0050、00631L、現金類給建議，其他持股一律「不動」**。純前端，後端不動。

設計與算法、三個情境（golden）在 `ARCHITECTURE.md`「回落煞車 — 以整個流動資產判斷曝險、分桶顯示」。**開工前請先讀完該章**；本任務書只列做什麼，不重複推導。

## Requirements
1. **分桶與範圍（新檔 `Portfolio/drawdownBrakeScope.ts`）**
   - `STRATEGY_EXCLUDED`（`[{ market: 'US', symbol: 'NTSD' }]`）：被排除的標的，**只有這一張表**決定誰被排除（DECISION-018）。
   - `classifyPositions(positions)`：REAL_ESTATE／LIABILITY 略過 → 被排除 → TW 的 `0050`／`00631L`（核心）→ `exposureFactor === 0`（現金類：全部 CASH 含外幣，加債券／貨幣型）→ `exposureFactor === 1`（其他一倍商品）→ 其餘（其他倍數商品，含反向）。
   - 曝險一律用 `PositionDto.exposureFactor × marketValueTWD`，**不得在前端另寫倍數規則**。核心的倍數若不是 0050＝1、00631L＝2 → 視為資料異常：不給建議並提示。
   - 回傳各桶市值、`othersValue`（其他一倍＋其他倍數）、`othersExposure`（Σ市值×倍數，可為負）、被排除持股清單、範圍內 `priceFetchFailed` 的標的。
2. **`advise()` 改寫（`drawdownBrake.ts`）**：輸入型別 `StrategyHoldings` → `ScopeHoldings`（`0050`、`00631L`、`cash`、`othersValue`、`othersExposure`），算法照 `ARCHITECTURE.md` 的 12c 公式（`C`、`A`、`c_min = min(C, k·A)`、`e* = E_t·A − X_o`、`clamp(e*, 0, 2I)`、composition 分段、`E_reach`）。`Advice` 新增 `scopeAssets`（A）、`eTarget`（等級表的目標）、`eReach`、`aboveTarget`、`capped`、各桶目標市值；觸發改比較 `|E_act − E_reach| > REBALANCE_BAND`。**`composition`、`PROFILES`、`REBALANCE_BAND`、`roundHalfAwayFromZero`、狀態機、固定參數一律不動**（DECISION-013）。
3. **12a 對帳**：
   - 沒有被排除持股時，卡片 E ＝ 頁面「水位」（`exposureOfLiquidAssetsPct / 100`，差異僅四捨五入）。
   - 有被排除持股時顯示對帳行：「流動總資產 X（水位 w%）；不含 NTSD Y；策略範圍 Z → 曝險 E」（NTSD 名稱取自 `STRATEGY_EXCLUDED`／持股名稱，不要寫死在文案裡）。沒有被排除持股時**不出現**此行（A21，不出現空行）。
4. **12b 分桶表**（取代現有的「目標配置：…｜策略總資產…」小字與 `strategyShareOfLiquidAssetsPct` 那一行）：
   - 列：0050、00631L、其他一倍商品、其他倍數商品（有才顯示）、現金類。每列：目前市值、目前曝險貢獻、目標市值（核心與現金類才有）、建議（加碼／減碼／不動＋金額；0050／00631L 另含整數股數，先賣後買）。各列市值合計＝A（單元測試鎖定）。沒有的桶不顯示（不出現空列／0 股／NaN）；整個範圍資產為 0 → 不顯示建議。
   - 現金類的金額變動＝ −Σ(實際建議股數×價格)。
   - 其他持股列的建議欄固定顯示「不動（由您自行決定）」，並有「未經回測、僅計入整體曝險、不給建議」的提醒。
   - 手機（360／400px）不得橫向捲動（建議用堆疊式列，不要寬表格）。
5. **12c 標示**：
   - `aboveTarget`：「即使 0050／00631L 全部轉為現金，整體曝險仍有 X 倍，高於目標 Y 倍；差額來自您自行決定的其他持股」。
   - `capped`：沒有其他持股時沿用原文「現金比例過高，無法達到此曝險」；有其他持股時用「受其他持股與最低現金限制，整體曝險最高只能到 X 倍（目標 Y 倍）」。
   - 核心已無可調整、偏離 > 0.10 → 「不動」＋上述標示（不得出現無法執行的建議）。
   - 字句為 Tech Lead 提議，PM 可改；放在 `drawdownBrakeCopy.ts` 常數，並有測試。
6. **績效揭露（R8）**：回測績效區塊標明「依 0050＋00631L＋現金回測，不是整個流動資產的績效」；固定警語 `DISCLAIMER` 一字不改、所有狀態常駐。
7. **移除策略現金**（A19）：`BrakeSettings.strategyCashTWD`、設定面板的輸入欄、`sumTwdCash`、「請先輸入策略現金」提示、`strategyShareOfLiquidAssetsPct` 與卡片上「此策略部位占總資產 X%」那一行（TASK-042 的需求 11，被需求 12 取代）。`sanitizeBrakeSettings` 遇到舊 localStorage 的 `strategyCashTWD` 要忽略、不報錯（有測試）。
8. **資料防護**：
   - 範圍內任一持股 `priceFetchFailed` → 列出標的、顯示「N 檔持股價格取得失敗，暫時無法算出建議」，**不給建議**（A 與 E 會被失敗的 0 值扭曲）。沒持有的 0050／00631L 仍以 `useLookupStockQuery` 取價以算買進股數（A4）；取價中顯示骨架、取不到則提示。
   - 分割防護（A8）與過期提示（7 天）**維持原樣**。
   - 沒有任何持股 → 沿用目前行為（卡片不顯示；A4 補充）。

## Acceptance Criteria
- `npm run lint`（0 warnings）、`npm run test`、`npm run build` 全過；`prettier --check` 通過。
- **退化對照**：`othersValue = othersExposure = 0` 時，`advise` 對現有 `drawdownBrake.test.ts` 所有案例結果相同（既有測試保留，只換輸入型別）；另加測試：3 等級×2 狀態下 `t/A` 等於 `composition(E,k)`。
- **Golden（ARCHITECTURE 三情境，價格 0050＝60、00631L＝30，平衡）**：S1 賣 0050 58,333 股／買 00631L 116,667 股，執行後再算為「不動」；S2（切換防守）賣 0050 41,667 股／賣 00631L 83,333 股，`aboveTarget`，執行後為「不動」＋標示；S3 為「不動」＋`capped` 標示，S3b 買 00631L 33,333 股。
- 沒有被排除持股的 fixture：卡片 E 與 `exposureOfLiquidAssetsPct / 100` 一致（四捨五入內）；加 NTSD 後：E ＝ `(marketExposureTWD − 1.5×NTSD) / (liquidGrossAssetsTWD − NTSD)`，對帳行出現；改 NTSD 金額只改對帳行、不改 E、桶與建議。
- 分桶各列市值合計＝A；其他一倍／其他倍數持股不論進攻、防守、再平衡都「不動」且沒有 orders；改其他一倍商品金額會改變 E 與核心建議。
- 範圍內有 `priceFetchFailed` 持股 → 顯示失敗標的、無建議；核心 `exposureFactor` 異常 → 無建議。
- 舊 localStorage 含 `strategyCashTWD` 不報錯；UI 找不到策略現金輸入。
- 版面：1280／400／360px、進攻／防守、展開風險說明與進階設定後 `scrollWidth === innerWidth`；PR 附桌機與手機截圖（含有被排除持股、有其他持股的情境）。
- 後端、`DrawdownBrakeDto`、`portfolioApis` 契約**沒有變更**。

## Technical Notes
- 只動 `jackyaiapp.client/src/Portfolio/` 與其測試；`drawdownBrakeCopy.ts` 的文案常數集中管理（PM 要改字只改一處）。
- 浮點比較（`e* < 0`、`e > 2I`）請用小容差（例如 1e-6 的相對 TWD 值）；`A <= 0` 時不顯示建議。
- 不要把 `.agent/` 狀態檔放進程式 PR（容易與 PM／Tech Lead 的 master 更新衝突）；依 Workflow conventions 直接推 master。
- 價格示意值與情境金額只用於測試，不是 Boss 的實際持股。

## Dependencies
無（TASK-042 已合併）。**TASK-043（重新整理按鈕）應排在本任務之後**（同一張卡片）。與 TASK-045（Finance 頁圖）檔案不重疊，可平行。

## PR
（待填）

## Result
（待填）

## Review
（待填）
