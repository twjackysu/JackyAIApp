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
8. **Google 日曆（Studio AI 的 `google_calendar_events`）用的是既有 Google 帳號連線**，scope 原本就有 `calendar.readonly`，不用改設定。合併後請用自己的帳號在 /connectors 連一次 Google，再問 Studio「我今天有什麼行程？」確認 production 的 Google OAuth app 真的有日曆權限（若 Google 回 403，工具會請使用者重新連線；那就要到 Google Cloud Console 確認 OAuth consent screen 的 scope 與 Calendar API 已啟用）。
9. **本站 MCP server（`/api/mcp`）實測**：合併後到 /connectors「讓其他 AI 使用本站」產生一個唯讀 token，複製 Claude Code 那行指令執行，再問 Claude Code「用 jacky-studio 查 serendipity」。若連不上：先看 Cloudflare 是否擋了非瀏覽器流量（Bot Fight Mode／WAF），需要的話替 `/api/mcp` 加例外；App Service 的 access restriction 只收 Cloudflare，不受影響。Claude Desktop 的設定經 `npx mcp-remote`，需要電腦上有 Node.js。**也請試 Claude.ai 的自訂 connector**（只貼 `https://apps.jacky-studio.com/api/mcp`，走本站 OAuth 與同意頁），以及 ChatGPT 的開發者模式 connector（如果你的帳號有）；失敗時記下 client 顯示的錯誤與 App Insights 裡 `/api/oauth/*` 的請求。連上後在 Claude.ai 問「我的投資組合現在怎麼樣？」：回答下方應該出現 Jacky Studio 的持倉表格卡片（MCP App，第 9 輪）；沒出現也不影響文字回答，請記下 Claude.ai 是否列出了 `ui://jacky-studio/card.html` 這個資源。另外，Cloudflare 若有 WAF 規則，`/api/oauth/register`、`/api/oauth/token` 也要放行（本站自己每個位址每分鐘限 30 次）。
10. **本站 MCP server 多了 `read_dashboard`（唯讀，第 12 輪）**：外部 AI（含已經發出的唯讀 token）可以讀你自己建立的 dashboard 的數字，資料可能來自你的自訂 SQL Server／ADX 資料源。不想開放的話，到 /connectors 的「本站功能」把「讀取 dashboard」設成停用（站內 Studio 也會一起停用）。
