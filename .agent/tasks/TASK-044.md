# TASK-044：加權指數歷史日線端點（Stock Intelligence「過去 5 年同期間走勢圖」的資料來源）（#431 延伸）

## Owner
Backend Engineer

## Status
MERGED（PR #440，Boss 於 2026-10-11 合併；Build and deploy run 529 成功）

## Priority
MEDIUM（PM：需求「Stock Intelligence 大盤走勢比較圖」A 圖；與回落煞車互不依賴）

## Objective
提供**全站共用、快取**的加權指數（`^TWII`，價格指數，不含息）近 6 年日線收盤，供前端畫「同一段月/日、每年一條線」的圖（TASK-045）。使用者數增加不得放大對 Yahoo 的請求。設計理由見 `ARCHITECTURE.md`「Stock Intelligence 大盤走勢比較圖」。

## Requirements
1. **端點**：`GET /api/finance/market-index-history`（`FinanceController`，**匿名**，與同控制器的 `market-overview` 一致；`/finance` 本來就不需登入）。無參數（第一版只做 `^TWII`，不接受任意 symbol，避免變成 Yahoo 代理）。回應（`responseFactory.CreateOKResponse`）：
   ```
   { symbol: "^TWII", name: "加權指數（價格指數，不含息）", source: "Yahoo Finance",
     asOf: "2026-10-08",                      // points 的最後一個交易日
     points: [ { date: "2021-01-04", close: 14138.24 }, ... ] }   // 升冪，約 1,450 筆
   ```
2. **資料取得**：新增 provider（放 `Services/Finance/DataProviders/`，沿用 `MarketOverviewProvider` 的作法）：`https://query1.finance.yahoo.com/v8/finance/chart/%5ETWII?range=6y&interval=1d`，走 `ExternalReadOnly` named client。解析 `chart.result[0].timestamp[]` 與 `indicators.quote[0].close[]`：
   - 跳過 `close` 為 null／非有限數／≤ 0 的日子；日期以**台北時間（UTC+8）**換算成 `DateOnly`；同日重複取最後一筆；排序升冪。
   - **當日未收盤的那一根不得入列**：台北時間今日且（非交易日、或時間 < 14:00）→ 丟棄今日（Yahoo 盤中會回傳進行中的 bar）。時間用固定 UTC+8 與 `TimeProvider`。
   - **健全性檢查**（失敗＝整份視為無效，走下方 fallback，不局部修補）：點數 ≥ 1,000；最後日期距今 ≤ 10 天；相鄰兩日漲跌幅絕對值 ≤ 15%（證交所單日漲跌幅限制 10%，加權指數歷史最大單日約 ±9.7%，超過視為資料錯誤）。
3. **快取**：`IMemoryCache`，**TTL 1 小時**，單飛（`SemaphoreSlim`／既有 single-flight 慣例），全站共用一份 → Yahoo 請求 ≤ 24 次／天，與使用者數無關。另保留「最後一次成功結果」（記憶體，最長 7 天）：重新取得失敗或無效時**回傳該份**（`asOf` 如實反映，前端會顯示資料日期）；連最後成功結果都沒有 → 回錯誤。
4. **錯誤**：新增 `ErrorCodes.MarketHistoryUnavailable`，沿用既有 `CreateErrorResponse(code, message)` 慣例；前端據此顯示「資料暫時無法取得」。不得回傳部分資料或空陣列冒充成功。
5. **LiveContract 測試**（`LiveContracts/`，加入同一個 Yahoo 契約測試類別旁）：`^TWII` `range=6y` 回 ≥ 1,000 點、升冪、全為正數、最後日期距今 ≤ 10 天。只驗 shape，不驗數值。更新根 `CLAUDE.md` Testing Status 的來源清單（Yahoo 條目已有，補上「6y 日線」即可）。

## Acceptance Criteria
- Offline 測試：解析（null close、重複日、亂序、非正數）、今日 bar 規則（交易日 14:00 前丟棄／14:00 後保留／週末丟棄）、健全性（點數不足、最後日期過舊、單日 +20% 皆判無效）、快取（同窗內多次呼叫只打 1 次上游）、單飛（並行 20 次只打 1 次）、失敗時回傳最後成功結果、無最後成功結果時回錯誤、Controller 匿名可呼叫且回應契約如上。
- `dotnet test --filter "Category!=LiveContract"` 全綠；無 DB／migration、無 AI、無 `[UsageLimit]`。
- 回應有經過既有 response compression（確認 `application/json` 在壓縮清單內；約 40 KB → 約 10 KB）。

## Technical Notes
- 沙箱連不到 Yahoo（egress 403）：格式依 GitHub Actions LiveContract 驗證。參考資料：Boss 的 `回測數據/tw_backtest/data/TWII.csv`（同樣是 Yahoo `^TWII`，1997-07-02 ~ 2026-10-08，7173 筆；Tech Lead 檢查：升冪無重複、無非正數、>6 天的空檔僅 31 處且皆為農曆年假、|單日漲跌| > 7% 僅 6 次且皆為真實大波動）。**可作為解析測試的樣本來源，但不要把整份 CSV 放進測試專案**（取一小段即可）。
- 未來若要 B 圖（相似區段，需 1997 起 29 年資料）：另案決定是否內嵌歷史快照＋只向 Yahoo 補差，**本任務不做**。
- 不要動 `MarketOverviewProvider`（當日報價）與回落煞車的任何東西（TASK-036 的報酬指數資料／DECISION-016）。

## Dependencies
無。前端 TASK-045 以上述回應契約平行開發。

## PR
#440（branch `claude/exciting-cray-9gj33o`，2026-10-11）

## Result
2026-10-11 Backend Engineer：
- **端點**：`GET /api/finance/market-index-history`（`FinanceController`，匿名，無參數）。回 200 `{ symbol, name, source, asOf, points: [{ date, close }] }`（升冪）；沒有任何資料 → `ErrorCodes.MarketHistoryUnavailable`（`1050301` → HTTP 503）。無 DB／migration、無 AI、無 `[UsageLimit]`。
- **`MarketIndexHistoryProvider`**：Yahoo `^TWII` `range=6y&interval=1d`（`ExternalReadOnly`）。跳過 null／非有限／非正數 close、台北時間（UTC+8）換日期、同日取最後一筆、升冪、close 四捨五入 2 位；台北今日的 bar 在週末或 14:00 前丟棄；整份健全性檢查（≥ 1,000 點、最後日期距今 ≤ 10 天、相鄰日漲跌 ≤ 15%、`timestamp[]`／`close[]` 等長），失敗＝整份作廢。
- **`CachedMarketIndexHistoryProvider`**（singleton 裝飾器）：`IMemoryCache` 1 小時＋`SingleFlight`；最後成功結果保留 7 天，重新取得失敗時回傳該份。
- **與任務書字面不同或需 Tech Lead 確認之處**（細節在 PR #440 描述）：(1) 另加「失敗後 5 分鐘不再打 Yahoo」；(2) 選 HTTP 503＋錯誤碼 `1050301`，前端 `apis/types.ts` 的 enum 需補（TASK-045）；(3)「非交易日」只判斷週末，未查 `ITwseHolidayCalendar`；(4) 兩個陣列長度不同整份作廢；(5) 用 `SingleFlight` 而非自建 `SemaphoreSlim`；(6) DTO 用 `sealed record`。
- **測試**：本環境以 apt 安裝 .NET 10 SDK 後實際執行——`dotnet test -c Release --filter "Category!=LiveContract"`：**967 通過、0 略過、0 失敗**；新增 66 個離線測試連跑 12 次無偶發失敗。另做 13 項變異測試（今日 bar 規則與邊界、週末、UTC+8、點數／過舊／漲跌門檻、重複日、四捨五入、長度檢查、TTL、退避、SingleFlight），每項都有測試失敗。
- **LiveContract**：沙箱連不到 Yahoo（egress 403），無法本機執行；已在本分支（head `5b0d56d`）手動 dispatch `contract-tests.yml`（run 38126581317）→ **成功**，含新增的 `^TWII` 6y 日線檢查（走正式 `Parse`）。只取得整個 job 的結論，未取得該測試個別輸出（實際點數、`asOf`）。
- **PR CI（head `63fe329`，run 38126661498）**：`build-and-test-server` ✅、`lint-and-build-client` ✅。尚待 Tech Lead 技術審查。
- **未驗證**：response compression 只驗了框架預設 MIME 清單涵蓋 `application/json`，未量實際位元組數。

## Review
**Tech Lead 事後審閱（2026-10-11，排程執行第 3 次；PR 由 Boss 先行合併並已部署，當時未走審閱）**
- **已讀** `FinanceController.GetMarketIndexHistory` 與 `CachedMarketIndexHistoryProvider`：端點匿名但**無任何參數**（不是 Yahoo proxy）；回應來自單一快取（1 小時新鮮＋`SingleFlight`＋失敗後 5 分鐘退避＋最後成功副本保留 7 天），上游請求量與訪客數無關（≤ 24 次／天）。公開端點的濫用面僅是讀記憶體，**成本與安全無疑慮**。
- Engineer 的 6 項差異可接受：(1) 失敗退避 5 分鐘（比任務書更保守）；(2) HTTP 503＋`1050301`；(3)「非交易日」只判斷週末，國定假日 Yahoo 本來就不產生 bar，無影響；(4) 兩陣列長度不同整份作廢；(5) 用既有 `SingleFlight`；(6) `sealed record`。
- 967 項離線測試通過；LiveContract（含 `^TWII` 6y 日線，走正式 `Parse`）手動 dispatch run 38126581317 成功。
- 未驗證：回應壓縮後的實際位元組數（約 1,500 點 JSON，量級數十 KB，可忽略）。
- 結論：無須追加修正。
