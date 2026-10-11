# TASK-037：回落煞車 — 前端（配置計算、加減碼提醒卡片、持股資料防護）（#431，第一階段）

## Owner
Frontend Engineer

## Status
PR_OPENED

## Priority
HIGH（Requirement：「0050 + 00631L『回落煞車』」第一階段；PM 標 HIGH）

## Objective
在 Portfolio 頁新增「回落煞車」卡片：依後端訊號（TASK-036）、使用者選的風險等級、持股快照與策略現金，顯示目前狀態、目標配置，以及「加碼／減碼／不動」與各標的建議買賣股數。**僅在 `drawdownBrakeEnabled` 為 true 時出現**（Q1 未決前只有 admin）。

設計與 API 契約見 `ARCHITECTURE.md`「回落煞車」一節；規則原文以 Issue #431（§2~§4、§9、§10）為準，**固定參數／禁止項見 DECISION-013，不得更動或新增選項**。

## Requirements
1. **純函式** `Portfolio/drawdownBrake.ts`（逐字依 #431 §3、§4 與參考實作）：
   - `PROFILES`：保守 (1.0, 0, 0.2)、平衡 (1.4, 0, 0.2)、積極 (1.8, 0, 0.1)（進攻 E、防守 E、k）；預設「平衡」。進階：防守 E 可改 0.3（**只有 0 與 0.3 兩個選項**）；k 為進階數值輸入（0 ~ 0.5）。**不得出現防守 E ≥ 0.5 或「不煞車固定槓桿」選項。**
   - `composition(E, k)`：`E ≤ 1-k` → `{0050:E, 00631L:0, cash:1-E}`；否則 `E=min(E,2(1-k))` 並回傳 `truncated=true`、`{0050:2(1-k)-E, 00631L:E-(1-k), cash:k}`。
   - `advise(prevState, state, profile, holdings, prices, band=0.10)`：狀態切換 → 一定調整；否則 `|E_actual − E_target| ≤ band` → 「不動」；`action = E_target < E_actual ? 減碼 : 加碼`；股數 `round((V·w − 市值) / 價)`，**四捨五入用 half-away-from-zero**（不要用 JS `Math.round` 對負數的 half-up，也不是 Python 銀行家捨入）；賣出排在買進之前；所有股數皆為 0 → 「不動」（PM 假設 A3）。`V` 為 0 時不得除以零。
   - `loadBrakeSettings/saveBrakeSettings`（`utils/storage.ts`，key `portfolio.drawdownBrake`，格式 `{ profile, defenseE, minCashRatio?, strategyCashTWD }`；同 `rebalancingTarget.ts` 的 per-browser 偏好先例）。
2. **API**：`apis/portfolioApis` 新增 `useGetDrawdownBrakeQuery`（型別放 `types.ts`，依 `ARCHITECTURE.md` 契約）；`accountApis/type.ts` 新增 `drawdownBrakeEnabled?: boolean`。卡片**只在 `drawdownBrakeEnabled` 為 true 時掛載並發請求**（`skip`）。
3. **卡片** `Portfolio/DrawdownBrakeCard.tsx`，掛在 `Portfolio/index.tsx` 的 `RebalancingCard` 附近：
   - 風險等級選擇、進階（防守 E、k）、**策略現金輸入**（NT$；並列顯示使用者 TWD 現金部位合計「供參考」，**不自動採用**）。
   - 目前狀態（進攻／防守）、資料截止日 `asOfDate`、距近一年高點 `drawdownPct`、距煞車觸發點 `distanceToTriggerPct`、最近切換日 `lastSwitchDate`；`isStale` → 「資料未更新」；`isInitializing` → 「資料準備中」且不顯示建議。
   - 文案依 #431 §10（減碼／加碼／不動／再平衡四種）；有效曝險、目標配置、各標的買賣股數。`composition` 截斷時顯示「現金比例過高，無法達到此曝險」。
   - 若回應含 `estimate` 非 null → 顯示「預估訊號（盤中）」標示並以預估狀態計算；`estimateCorrected` → 顯示「訊號已更正」。（第二階段才會有值，但本任務須能渲染並有測試。）
   - 「風險等級說明」（對話框或可展開區）：必含 #431 §10 三點（盤整年可能比 0050 虧更多；00631L 曾單日跌 20%、煞車收盤才判斷擋不住急跌；平均每年約觸發 3 次進出、約 29% 時間防守）。顯示任何回測績效時**同時顯示期間與最大回撤**，不得只突顯單一最高報酬；第一階段只在收盤後提醒，績效欄用「隔日成交」版本（以上為 PM 提議 R3／R4，**Boss 尚未確認**；Boss 若改指示，只影響此文案區塊）（平衡 15.4% / −37%、保守 11.9% / −27%、積極 18.1% / −45%；數字取自 #431 §2，**實作時逐一對照原文，不得憑記憶**）。
4. **固定警語**：「依歷史回測規則產生的訊號，過去績效不代表未來，不構成投資建議。」於卡片所有狀態常駐可見、不可關閉；並含「2014 年以前的 00631L 績效為模擬值」註記。
5. **持股資料防護（PM 的 R2）**：
   - 持股取自 `summary.positions`（`assetType==='STOCK' && market==='TW' && symbol ∈ {'0050','00631L'}`），用 `quantity` × `currentPrice`；顯示「持股資料日期」＝`summary.effectiveDate`。
   - **分割硬性防護**：常數表 `SPLIT_EVENTS = { '0050': {date:'2025-06-18', ratio:4}, '00631L': {date:'2026-03-31', ratio:22} }`（來源 Boss #431，PM 未驗證；請以公開資訊盡力交叉確認，無法確認就照此並在 PR 註明）。若使用者持有該標的且 `effectiveDate` 早於其分割日 → **不顯示任何買賣股數**，改顯示「你的持股快照建立於 {標的} 分割（{日期} 1 拆 {N}）之前，股數可能未調整，請先更新持股」。
   - 軟性提示：`effectiveDate` 距今 > 7 天 → 「持股資料可能已過期，請先更新持股」（7 天為 PM 假設，常數集中一處以便調整）。
   - 沒有任何 0050／00631L 持股、只有策略現金 → 仍顯示目標配置與建議買進股數（PM 假設 A4）。

## Acceptance Criteria
- vitest：
  - `composition`：`(1.4,0.2)→{0050:0.2, 00631L:0.6, cash:0.2}`；`(1.0,0.2)→{0.6, 0.2, 0.2}`；`(1.8,0.2)` 截斷為 E=1.6 → `{0, 0.8, 0.2}` 且 `truncated`；`(0,0.2)→{0,0,1}`；`(1.8,0.1)→{0, 0.9, 0.1}`；`(0.3,0.2)→{0.3, 0, 0.7}`。
  - `advise`：狀態未變且偏離 ≤ 0.10 → 「不動」且無 orders；狀態切換 → 一定有 action；偏離 > 0.10 → 再平衡；負數 .5 的捨入；賣在買前；全 0 股數 → 「不動」；V=0 不爆。
  - 分割防護：快照早於 0050／00631L 分割日 → 無股數、顯示對應提示；快照晚於分割日 → 正常；未持有該標的 → 不因它而擋。
  - 卡片元件測試：警語常駐（含各狀態：載入中、isInitializing、isStale、錯誤、有 estimate）；`drawdownBrakeEnabled=false` 不渲染且不發請求；選單中沒有防守 E ≥ 0.5 或固定槓桿選項；estimate／estimateCorrected 標示。
- 手機寬度（≤ 400px）無橫向捲動（沿用其他 Portfolio 卡片的做法，附截圖或說明）。
- UI 不得出現「credit／點數／付費」字樣；不呼叫 AI；不消耗 UsageLimit。
- `npm run lint`、`npm run test`、`npm run build` 全過。

## Technical Notes
- 後端契約由 TASK-036 提供；開發期間可用 mock。**整合驗證在 TASK-036 合併後**（PR 描述註明是否已對真實端點驗證）。
- 若 TASK-036 的 DTO 與 `ARCHITECTURE.md` 契約有出入，以 `ARCHITECTURE.md` 為準並回報 Tech Lead。
- 已知權衡：設定存 localStorage（per-browser，換裝置需重設）— 見 `ARCHITECTURE.md`。
- 若 Boss 於 Q3 改變「策略現金」的指定方式，影響限於輸入區塊，核心計算不變。
- 範圍外：站外通知、伺服器端設定儲存、回測圖表、Portfolio 全域的股票分割處理（既有限制，另案）。

## Dependencies
TASK-036 的 API 契約（已定義於 `ARCHITECTURE.md`，可平行開發；合併順序不拘，整合驗證需 036）。

## PR
https://github.com/twjackysu/JackyAIApp/pull/432（branch `claude/nice-allen-x7oovf`）

## Result
Frontend Engineer（2026-10-10）：
- 實作完成：`Portfolio/drawdownBrake.ts`（PROFILES／composition／advise／設定）、`drawdownBrakeHoldings.ts`（持股擷取、分割防護、過期提示）、`drawdownBrakeCopy.ts`（警語／§10 文案／風險說明／隔日成交版回測）、`DrawdownBrakeCard.tsx`；`portfolioApis.getDrawdownBrake`、`DrawdownBrakeDto`、`User.drawdownBrakeEnabled`。
- 驗證：`npm run lint`／`npm run test`（49 檔 396 項，新增 4 檔 96 項）／`npm run build` 全過；360px 與 400px 四個情境（含極端數字、進階設定與風險說明展開）實測無橫向捲動。
- **未對真實端點驗證**（TASK-036 尚未合併）；合併後需整合驗證。
- `SPLIT_EVENTS` 已以公開資訊交叉確認，與 Boss 常數相符（0050 2025-06-18 1 拆 4；00631L 2026-03-31 1 拆 22，皆為恢復交易日），未修改。
- 與參考實作／範例的差異與待確認事項見 PR 描述：`E_target` 取截斷後 E、`prevState=null` 視為 OFFENSE、A4 以既有 `lookup` 端點取價、再平衡文案句型、「隔日成交」欄期間 1997–2026 為推定（請 PM／Boss 確認）、無任何持股者看不到卡片。

## Review
2026-10-11 Tech Lead 審查 PR #432（head `42f8fbb`）：**技術審查通過 → READY FOR BOSS REVIEW**（不代表核准合併；Status 欄由 Engineer 分支維護，這裡不動以免合併衝突）。

- **規則忠實度**：`composition`／`advise` 對照 #431 §3、§4 與 §7 參考實作無誤；`PROFILES`、`DEFENSE_E_OPTIONS=[0,0.3]`、k 範圍 0~0.5 正確；沒有防守 E ≥ 0.5 或固定槓桿選項，且 localStorage 讀取時 `sanitize`（手動改成 ≥0.5 也不生效）。捨入為 half-away-from-zero；賣先於買；全 0 股數 → 「不動」；`V=0` 不除以零。
- **對參考實作／§10 範例的偏離——皆接受**：(1) `E_target` 取**截斷後**的 E（§3 明文「以截斷後的 E 計算」；預設三個等級不截斷，結果相同）；(2) `prevState=null` 視為 OFFENSE；(3) 沒持股者（PM A4）以既有 `lookup/TW/{symbol}` 取價，查不到就顯示無法取得、不猜；(4) 再平衡句型改為只陳述「實際曝險 vs 目標」——因為切換日後沒照做時也會走到這條，原範例的「漲多、升到」會說錯（減碼／加碼／不動三種與範例逐字相同）。
- **安全／合規**：卡片僅在 `drawdownBrakeEnabled` 為 true 時掛載並發請求（後端另有 403，前端只是 UX）；固定警語與「2014 年以前 00631L 為模擬值」常駐於每個狀態（含載入中、錯誤、資料準備中、資料未更新、預估）；無 AI、無 UsageLimit，無 credit／點數／付費字樣。
- **分割防護（R2）**：`SPLIT_EVENTS` 與 Boss 常數相符，Engineer 以公開報導交叉確認（皆為恢復交易日）；快照早於分割日 → 不顯示股數；快照等於分割日放行；無日期保守擋下；未持有該標的不擋；>7 天顯示過期提示。
- **測試**：lint 0 warnings、vitest 396 項（新增 96）、build 全過；PR CI 綠；360／400px 無橫向捲動（Engineer 以 Playwright 實測）。
- **待確認（不阻擋，詳見 TECH_LEAD.md）**：(a) 「歷史回測（1997–2026，隔日成交版本）」的期間為依 #431 §2 欄位順序**推定**，#431 只在「回測年化／最大回撤」欄標 1997–2026；**開放給一般使用者（Audience=All）前，Boss 須確認**，只需改 `drawdownBrakeCopy.ts` 的 `BACKTEST_PERIOD`。(b) 目前沒有任何持股的使用者看不到此卡片（卡片掛在「有持股」才渲染的區塊內）；Q1／Q3 決定後再看是否需要入口。(c) 再平衡句型與 §10 範例不同（見上），Boss 若要逐字照範例，只改 `adviceMessage`。
- **整合驗證**：TASK-036 合併後，admin 需實際走一遍（`GET /api/portfolio/drawdown-brake` 與 `drawdownBrakeEnabled` → 卡片顯示真實訊號）。建議合併順序：#433 → #432（若單獨先合 #432，`drawdownBrakeEnabled` 恆為 undefined，卡片不會出現，無害）。
- **QA**：卡片僅 admin 可見，且元件測試涵蓋各狀態；不另派 QA，由 Boss 在 #433 部署後於手機寬度目測一次。
