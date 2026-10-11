# TASK-042：回落煞車卡片 — PR #432 後續修正（回測數字更正、模擬值註記、策略部位占比、解衝突）（#431）

## Owner
Frontend Engineer（PR #432 已合併；從最新 master 開新分支與新 PR）

## Status
MERGED（PR #436，Boss 於 2026-10-11 合併；需求 11 的「策略部位占比」隨需求 12 由 TASK-046 移除）

## Priority
HIGH（PM 產品面不一致；目前只有 admin 可見所以不阻擋合併，但**必須在 `Audience=All` 之前完成**；且 PR #432 目前有合併衝突，Boss 合併前最好先處理）

## Objective
把 PR #432 的畫面文案對齊 Boss 於 2026-10-11 更正的回測數字與資料限制說明，補上需求 11，並消除 PR 與 master 的合併衝突。

## Requirements
1. ~~**解合併衝突（僅文件）**~~ **已完成（2026-10-11）**：Tech Lead 已把 master 併入 PR #432 分支並由 Boss 合併（`15c67e4`），本項不必再做。原說明：：把 `origin/master` merge 進 PR #432 的分支（**merge，不要 rebase／force-push**）。Tech Lead 已用 `git merge-tree` 驗證：與 master 唯一的衝突檔是 `.agent/status/PROJECT_STATUS.md`（master 檔尾 PM 追加了數段，PR 也在檔尾追加一段）→ **兩邊都保留**，不要改動 PM 的段落。程式碼與 PR #434（`RebalancingCard`／`rebalancingTarget`）**沒有重疊**，`Portfolio/index.tsx` 會自動合併。
   - 之後請不要再把 `PROJECT_STATUS.md`／其他 `.agent/` 狀態檔的變更放進程式 PR（容易互相衝突）；依 Workflow conventions 直接推 master。
2. **`drawdownBrakeCopy.ts`（及 `drawdownBrakeCopy.test.ts`、`DrawdownBrakeCard.test.tsx` 中寫死的斷言）**，數字來源＝`回測數據/tw_backtest/results/reproduce_spec.txt`（隔日成交 = `lag2` 欄）與 `REQUIREMENTS.md`「Boss 更正的回測數字」，**PR 描述請逐項列出你對照的行**：
   | 常數 | 舊 | 新 |
   |---|---|---|
   | `BACKTEST_NEXT_DAY.conservative` | 11.9 / −27 | **12.1 / −27** |
   | `BACKTEST_NEXT_DAY.balanced` | 15.4 / −37 | **15.3 / −37** |
   | `BACKTEST_NEXT_DAY.aggressive` | 18.1 / −45 | **18.4 / −45** |
   | `RISK_POINTS[0]` 的 2011 年平衡等級 | −29%（0050 −18%） | **−28%**（0050 −18% 不變） |
   | 檔內註解的「收盤集合競價版本」 | 13.2/−23、17.4/−32、20.8/−39（k=0，已作廢） | **13.5/−24、17.2/−32、20.9/−40** |
   `RISK_POINTS[2]`（每年約 3 次進出、約 29% 時間防守）與 `BACKTEST_PERIOD`（1997–2026，Q5 已結案）**不變**；請順手確認 `reproduce_spec.txt` 的「exits per year 3.0, share of days in DEFENSE 29%」與其一致。
3. **`SIMULATED_NOTE`**（PM 假設 A7，Boss 可再改字）：涵蓋「2014 年以前的 0050 與 00631L 績效為依報酬指數模擬的數字；1997–2002 年的股利為估計值」。固定警語 `DISCLAIMER` **一字不改**（DECISION-013）。所有狀態（載入中／錯誤／資料準備中／資料未更新／預估／正常）都仍須顯示警語與註記（既有測試要保持）。
4. **需求 11：策略部位占總資產比例**（純資訊，**不得**影響訊號、目標配置、建議股數）：
   - 卡片顯示「此策略部位占總資產 X%」（小數 1 位）。
   - 分子＝0050 市值＋00631L 市值＋策略現金（即卡片已算的策略總資產 V）；分母＝`summary.liquidGrossAssetsTWD`（流動總資產：台股＋美股＋基金＋現金，不含不動產與負債；與頁面「流動資產」標題及 PR #434 同一數字）。
   - 分母 ≤ 0、缺資料、或 V 未知時**不顯示此行**（不得出現 NaN／Infinity／−）。
   - 放在目標配置附近，與固定警語不重疊；手機寬度（360／400px）無橫向捲動。
5. 版面確認（PM 要求）：PR #432 的卡片掛在再平衡建議之後，與 #434 後的再平衡卡片版面／文案不重疊；PR 描述附一張合併後的桌機與手機截圖。

## Acceptance Criteria
- `npm run lint`（0 warnings）、`npm run test`、`npm run build` 全過。
- 測試涵蓋：上表每個新數字、`SIMULATED_NOTE` 新內容、需求 11（正常值、分母 0、缺價格、V 為 0）。
- PR #432 狀態變為可合併（無衝突）。
- 不改 `composition`／`advise`／狀態機任何邏輯（DECISION-013）。

## Technical Notes
- 若 Boss 在你動手前已合併 PR #432：從最新 master 開新分支與新 PR 做同樣的修正，不要在已合併的 PR 上堆 commit。
- 重新整理按鈕與資料時間屬 TASK-043（依賴 TASK-041），**本任務不做**。

## Dependencies
無（PR #432 已 Tech Lead 審查通過）。

## PR
https://github.com/twjackysu/JackyAIApp/pull/436 （分支 `feature/task-042-drawdown-brake-copy-fixes`；#432 已合併，故為從最新 master 開的新 PR）

## Result
**實作摘要（2026-10-11 Frontend Engineer）**
- `drawdownBrakeCopy.ts`：`BACKTEST_NEXT_DAY` 改為 12.1/−27、15.3/−37、18.4/−45（對照 `reproduce_spec.txt` 的 `lag2` 欄）；`RISK_POINTS[0]` 的 2011 年平衡等級 −29% → −28%；檔內註解的收盤集合競價版本改為 13.5/−24、17.2/−32、20.9/−40（k=0 的舊數字標為作廢）；`SIMULATED_NOTE` 改為「2014 年以前的 0050 與 00631L 績效為依報酬指數模擬的數字；1997–2002 年的股利為估計值。」；`DISCLAIMER` 一字未動；`BACKTEST_PERIOD`、`RISK_POINTS[1]`/`[2]` 不變（`reproduce_spec.txt`：exits per year 3.0、share of days in DEFENSE 29% 與其一致）。檔頭過時註解（取自 #431 §2、R3/R4 待 Boss 確認）一併更新。
- 需求 11：`drawdownBrakeHoldings.ts` 新增純函式 `strategyShareOfLiquidAssetsPct`；`DrawdownBrakeCard.tsx` 在建議框內「目標配置…策略總資產」那行下方顯示「此策略部位占總資產 X.X%（分母為流動總資產，不含不動產與負債）」。分子＝`advice.totalValue`（V）、分母＝`summary.liquidGrossAssetsTWD`；分母 ≤ 0／缺資料／V 為 0／價格缺漏或查詢中／分割防護命中時整行不顯示。未改 `composition`／`advise`／狀態機（DECISION-013）。
- 測試：`drawdownBrakeCopy.test.ts`、`DrawdownBrakeCard.test.tsx`、`drawdownBrakeHoldings.test.ts` 更新並新增需求 11 各情境（正常值、分母 0／負／NaN／缺欄位、缺價格、查價中、V 為 0、分割、純資訊、隱私模式）；lint（0 warnings）、test（49 檔 423 項）、build 本機全過。
- 版面：以暫時 Vite harness（真實元件＋預填 RTK cache，未提交）驗證 1280／400／360px、進攻與防守、展開風險說明與進階設定後，`scrollWidth == innerWidth`（無橫向捲動）；再平衡卡片在上、回落煞車卡片在後，文案不重疊。

**與任務書的差異／自行決定（請 Tech Lead 確認）**
1. 占比顯示條件綁在「建議框存在」：連「沒持有該標的、查價失敗」這種 V 其實算得出的情形也隱藏（較保守）。
2. 占比屬「比例」，依 `showRatio` 在「只看損益」隱私模式隱藏（與再平衡卡片一致）；任務書未提。
3. 加了括號說明分母（流動總資產，不含不動產與負債），避免同頁出現兩種「總資產」。
4. 已知限制：策略現金為使用者自填，不一定已計入 Portfolio 現金，占比可能 > 100%，目前如實顯示、不截斷。
5. 第 1 項（解衝突）已由 Tech Lead 完成，未做。

## Review
**Tech Lead 事後審閱（2026-10-11，排程執行；PR 於開出 4 分鐘後由 Boss 合併，當時未走審閱）**
- 回測數字：`BACKTEST_NEXT_DAY`（12.1/−27、15.3/−37、18.4/−45）、2011 年 −28%／0050 −18%、`BACKTEST_PERIOD`＝1997–2026，皆已對照 `回測數據/tw_backtest/results/reproduce_spec.txt`（`lag2` 欄、`2011:` 行、`exits per year 3.0 … 29%` 行）逐項一致。`DISCLAIMER` 一字未動。
- 未讀完整 diff 的測試與版面；PR 描述自述 lint／423 項測試／build 皆過、400／360px 無橫向捲動。
- 需求 11 的占比（`strategyShareOfLiquidAssetsPct`、卡片上的「此策略部位占總資產 X%」）已被需求 12（DECISION-017）取代，**TASK-046 移除**。
- 結論：無須追加修正。
