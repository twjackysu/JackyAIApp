# FOR_JACKY — 只有你能處理的事

> 開發期間累積；合併前請逐條處理。

0. ~~GitHub Actions 停擺~~：2026-10-24 05:47 起 job 因「recent account payments have failed」不啟動，**10:14 左右已恢復**。停擺期間累積的後端 commit 已由 CI 驗證。
1. **Anthropic API key**：production 已有 `Settings:AnthropicApiKey`（Finance 在用）；Studio AI 與 Data Analysis 對話共用它，不需要新 key。注意用量會增加（每人每日預算不變）。
2. **確認 `dbo.Orders`（Data Analysis 預設資料源）的欄位**是否適合 demo（地區、日期、金額）。不適合的話 demo 會改用 `docs/ai-native/demo/` 的範例 CSV 上傳。
3. **既有 `GET api/connectors/{provider}/token`、`/refresh-token`** 會把原始 token 傳給瀏覽器（「複製 token」按鈕）。這次為了不退化沒有移除；建議合併後評估是否拿掉。
4. **MCP OAuth 不需要你註冊任何 OAuth app**：我們用 DCR（或 CIMD）向對方自動註冊。對方看到的網址是 redirect URI `https://apps.jacky-studio.com/api/connectors/callback/mcp`、client metadata `https://apps.jacky-studio.com/api/connectors/oauth/client-metadata`，都由既有的 `Connectors__CallbackBaseUrl` 推出，不用新設定。若 Cloudflare 有 Bot Fight Mode，CIMD 的抓取可能被擋（目前 DCR 優先，D-034，影響不大）。
5. **Connectors 重新放回首頁 portal**（D-011／D-020）：原本因 Google 帳號連線的 scope 問題隱藏，帳號連線那一塊沒有改。不想顯示的話把 `apps.tsx` 的 `portalDestinations` 拿掉 `CONNECTORS`。
6. **Studio AI 會呼叫你的 Anthropic key**（每則訊息照既有 chat 收費），字典查不到的字會由 OpenAI 產生一次（與字典頁相同）。
7. **合併後實測 Notion／Linear 的 OAuth 一鍵授權**（D-037）：開發環境連不到它們，流程只用假的授權伺服器測過。失敗時 /connectors 的 connector 會顯示原因（OAuth 錯誤碼），請把畫面上的訊息記下來。
