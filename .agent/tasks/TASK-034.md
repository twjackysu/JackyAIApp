# TASK-034

## Title
SEO Phase C: 單字頁 JSON-LD 結構化資料（DefinedTerm）+ sitemap lastmod

## Owner
Backend

## Status
PR_OPENED

## Priority
MEDIUM

## Objective
讓 Google 更容易理解並展示每個單字頁（#407 Boss 希望「apple」「apple 英文」等不帶品牌詞的查詢能被找到）。
結構化資料只是輔助，**不保證排名**。

## Requirements
- 在 `WordPageMetadataMiddleware` 既有注入流程中，於 `</head>` 前插入
  `<script type="application/ld+json">`，內容為 schema.org `DefinedTerm`
  （`name`=單字、`description`=`Word.SeoDescription`、`inLanguage`=`en`、`url`=canonical）。
- JSON 用 `System.Text.Json` 序列化（不得字串拼接），並處理 `</script>` 注入（`<` 以 `<` 跳脫）。
- `SitemapController` 每個單字 `<url>` 補 `<lastmod>`（若 Word 有更新時間欄位則用之；沒有就不加，不新增欄位／migration）。
- 失敗路徑沿用 TASK-020：任何例外 → 回傳未修改的 index.html。

## Acceptance Criteria
- Offline unit tests：JSON-LD 內容正確、惡意單字資料（`</script>`、引號、`$`）安全、未知單字無注入、lastmod 格式 ISO 8601。
- 不新增 AI 呼叫、不改 DB schema、不改 `<title>` 格式（title 格式待 Boss 決定，見 TECH_LEAD.md）。

## Technical Notes
- 預期收錄速度仍取決於 Googlebot；本任務不能取代 Search Console 操作與內部連結（TASK-033 已完成）。
- 驗證：部署後用 Google Rich Results Test 抽測 `https://apps.jacky-studio.com/english/dictionary/apple`。

## Dependencies
TASK-020、TASK-033 已 MERGED。

## PR
（待 Engineer 填寫）

## Result
- WordPageMetadataMiddleware：`</head>` 前注入 DefinedTerm JSON-LD（System.Text.Json 序列化，`<` 跳脫為 \u003C）。
- SitemapController：每個 `<url>` 加 `<lastmod>`（Word.LastUpdated，yyyy-MM-dd；無值則不加）。無 migration、無 AI 呼叫、title 不變。
- 測試：已新增 offline unit tests；本環境無 dotnet SDK（proxy 擋下載），**未能實際執行**，需靠 PR CI 驗證。

## Review
（待填）
