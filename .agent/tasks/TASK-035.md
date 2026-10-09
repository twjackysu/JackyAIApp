# TASK-035：單字頁 title 改為 `{單字} 英文意思、發音、例句 | English Coach`（#407 SEO）

## Owner
Backend Engineer

## Status
PR_OPENED

## Priority
MEDIUM

## Objective
Boss 於 #407 核准（2026-10-09，「2. 可以」）：單字頁 title 改為單字在前、含中文關鍵字，提高「apple 英文」類長尾查詢的命中機會。**排名仍不保證。**
此為對 TASK-019 先前定案格式（`English Coach - {單字}`）的 Boss 授權變更。

## Requirements
- 僅修改 `WordSeoTextFormatter.FormatTitle`（單一來源，`WordMapper.ToDto` 已呼叫；前端、`BotPreviewMiddleware`、`WordPageMetadataMiddleware` 皆讀 `SeoTitle`，不得在他處重複格式化）。
- 新格式：`{wordText} 英文意思、發音、例句 | English Coach`。
- 首頁 title（`English Coach - AI 英文學習與單字訓練`）與 Exam 等其他頁不變。
- 不改 DB（SeoTitle 為執行時計算，非儲存欄位；已確認 `WordMapper` 即時產生）、不新增 AI 呼叫。

## Acceptance Criteria
- 更新 `WordSeoTextFormatterTests`、`BotPreviewMiddlewareTests`、`WordPageMetadataMiddlewareTests` 中寫死的舊 title 斷言為新格式。
- `dotnet test --filter "Category!=LiveContract"` 全綠。
- 單字含特殊字元時，HTML 編碼行為不變（沿用既有測試）。

## Technical Notes
- 若前端有任何測試／snapshot 寫死舊 title，一併更新（grep `English Coach - `）。
- 部署後以 `https://apps.jacky-studio.com/english/dictionary/apple` 檢視 `<title>`；Google 重新抓取需時。

## Dependencies
TASK-034 已 MERGED（#427）。

## PR
https://github.com/twjackysu/JackyAIApp/pull/430（本機無 dotnet SDK，測試未執行，依 CI 為準）

## Result
（待填）

## Review
（待填）
