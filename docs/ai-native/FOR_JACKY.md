# FOR_JACKY — 只有你能處理的事

> 開發期間累積；合併前請逐條處理。

1. **Anthropic API key**：production 已有 `Settings:AnthropicApiKey`（Finance 在用）；Studio AI 與 Data Analysis 對話共用它，不需要新 key。注意用量會增加（每人每日預算不變）。
2. **確認 `dbo.Orders`（Data Analysis 預設資料源）的欄位**是否適合 demo（地區、日期、金額）。不適合的話 demo 會改用 `docs/ai-native/demo/` 的範例 CSV 上傳。
3. **既有 `GET api/connectors/{provider}/token`、`/refresh-token`** 會把原始 token 傳給瀏覽器（「複製 token」按鈕）。這次為了不退化沒有移除；建議合併後評估是否拿掉。
4. （待補：MCP OAuth 的 redirect URI、CIMD 文件網址——M3 時寫入）
