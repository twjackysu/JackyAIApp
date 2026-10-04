# TASK-029：匿名頁面用量統計（Application Insights 瀏覽器端）

## Owner
Frontend Engineer（含一個極小的後端設定端點；同一 PR 完成即可）

## Status
READY

## Priority
MEDIUM（Requirement：匿名用量統計，DECISION-010）

## Objective
Boss 能在 Azure Portal（Application Insights）看到按頁面路徑分群的造訪數、不重複使用者數、登入 vs 訪客、回訪率；資料不含任何可識別個人資訊。

## Requirements
1. 現況：後端已有 `UseAzureMonitor`（`Program.cs`，有連線字串才啟用），只涵蓋 server request；本站是 SPA，前端路由切換**不會產生 server request**，所以必須補瀏覽器端 page view。
2. 前端引入 `@microsoft/applicationinsights-web`，**以動態 `import()` 延後載入**（不進主 bundle、不阻塞首屏），在 router 路徑變更時送 `trackPageView`（name 用 route pattern 或 pathname，**不帶 query string**；字典單字頁 `/english/dictionary/{word}` 彙整為單一 name，避免數萬個 distinct page name）。
3. 連線字串來源：新增匿名端點 `GET /api/config/telemetry` 回傳 `{ connectionString }`（讀既有 `ApplicationInsights:ConnectionString` / `APPLICATIONINSIGHTS_CONNECTION_STRING`；空值回 `{ connectionString: null }`，前端即不初始化）。連線字串非機密（browser SDK 本來就公開），此做法免改 CI/build 變數。
4. 匿名：**不得**呼叫 `setAuthenticatedUserContext`、不送 email／name／Google ID；不載入廣告或第三方追蹤。登入 vs 訪客以自訂屬性 `isAuthenticated: true|false` 帶上（布林值而已）。
5. SDK 設定：關閉不必要的自動收集（`disableAjaxTracking`、`disableFetchTracking`、`disableExceptionTracking` 皆 true 以避免噪音與額外成本；後端已有 request/dependency 資料）；`enableAutoRouteTracking` 不用，改手動送 page view 以便控制 name。
6. 開發環境（`import.meta.env.DEV`）與連線字串為空時完全 no-op，不報錯。
7. 成本：僅 page view，流量極低，預期遠低於 5GB/月免費額度；PR 說明中附一行估算。

## Acceptance Criteria
- [ ] 路由切換各送一筆 page view（元件/hook 測試以 mock SDK 驗證：query string 被去除、單字頁彙整為單一 name、連線字串為 null 時不初始化、未登入/已登入屬性正確）。
- [ ] 後端端點有單元測試（有/無連線字串兩種）。
- [ ] 主 bundle 大小無明顯增加（SDK 在獨立 chunk；PR 附 build 輸出對照）。
- [ ] `npm run lint`、`npm run test`、`npm run build`、後端 `dotnet test --filter "Category!=LiveContract"` 全過。
- [ ] PR 說明附「Boss 在 Azure Portal 查看」的步驟（Usage → Users／Page views，依 name 分組；回訪率看 Retention）。

## Technical Notes
- 不使用 cookie 以外的追蹤；SDK 預設會用 cookie（`ai_user`、`ai_session`）存匿名 ID——**這是隱私揭露問題**，見下方 Out of Scope／待 Boss 決定，不在本 task 自行加文字。
- 參考既有 RTK Query 設定取得 config（或單純 `fetch` 一次，不需進 Redux）。
- 先確認 `App.tsx` 的 router 結構，hook 放在 Layout 層級即可，不做無關重構。

## Dependencies
無。實作前先在 master 推 `Status: IN_PROGRESS`。

## Out of Scope
- 隱私揭露／Cookie 告知文字（Tech Lead 已列入 TECH_LEAD.md 待 Boss 決定）。
- 逐用戶行為分析、自訂事件（按鈕點擊等）、A/B 測試、自建後台。

## Result
（待 Engineer 填寫）

## Review
（待 Tech Lead 填寫）
