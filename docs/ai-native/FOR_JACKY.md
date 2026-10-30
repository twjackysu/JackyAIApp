# FOR_JACKY — 只有你能處理的事

> 開發期間累積；合併前請逐條處理。

## 回國後建議的順序（第 58 輪整理）

下面 22 條大多是「知道就好」或合併後實測。依先後：

1. **合併前，在本機或看截圖決定 demo 資料**：2（`dbo.Orders` 欄位適不適合 demo）。
2. **合併前，要你拍板的產品決定**（不決定也能合併，維持現狀）：3（是否拿掉回傳原始 token 的舊端點）、5（Connectors 是否留在首頁）、10（外部 AI 能不能讀 dashboard）、15／18(b)（Whisper 長度計費、匿名查字典產生新字的上限）、17（「限定分享」改邀請制）。
3. **合併**：照 [SHOWCASE.md](SHOWCASE.md) 的「合回 master 的檢查清單」（不需要新 secret；4 個只新增的 migration；刪 `ai-native-ci.yml` 與 `.loop/`）。
4. **合併部署後 30 分鐘內實測**（順序就是風險高低）：13（Google／Microsoft 帳號連線仍能連，改到 master 的 callback）→ 8（Studio 讀 Google 日曆）→ 9（Claude Code／Claude.ai 連本站 MCP server，可能要調 Cloudflare）→ 7（Notion／Linear 一鍵授權）→ 12（MCP 官方目錄）→ 19（別人的 MCP App）。
5. **之後再看**：14（CSP）、11（.NET SDK 偶爾下載不到時重跑即可）。

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
11. **.NET SDK 10.0.403（2026-10-25 發布）一度下載不到**（第 25 輪）：`setup-dotnet` 的 `10.0.x` 會挑它，結果「Could not find .NET Core SDK with version = 10.0.403」。`ai-native-ci.yml` 暫時釘在 `10.0.402`，第 26 輪（10/26 清晨）已可下載、改回 `10.0.x`（合併前整個檔案會刪掉）。master 的 `CI.yml`、`contract-tests.yml`、部署 workflow 也是 `10.0.x`／`DOTNET_CORE_VERSION`，我沒有動；若你回來時 master 的 CI 或部署出現同樣的錯誤，通常只是微軟 CDN 還沒同步，重跑即可。
12. **/connectors 搜尋 MCP 官方目錄（第 29 輪，D-122）**：伺服器會從 App Service 連 `https://registry.modelcontextprotocol.io`（只讀、免金鑰）。合併後在 /connectors 目錄最後一張「找更多 MCP server」搜尋 `github` 確認有結果；若一直顯示「MCP 官方目錄暫時讀不到」，看 App Insights 的 `McpRegistry` 警告。每天的 contract test 也會多一項 `McpRegistryContractTests`。
13. **既有的 Google／Microsoft／Atlassian 連線多了一道檢查（第 41 輪，D-155）**：OAuth 回來時，瀏覽器登入的必須是按「連線」的那個帳號（防止別人把授權網址寄給你，讓你的帳號連進他的連線）。這改到 master 原有的 `ConnectorService.HandleCallbackAsync`。合併部署後請到 /connectors 實際連一次 Google（或 Microsoft），確認仍顯示 Successfully connected；若失敗，先確認沒有在授權途中換帳號或登出。
14. **建議（可選）加 Content-Security-Policy 當第二道防線**（第 41 輪，D-157）：AI 回答已不載入任何圖片，但若想再保險，可在 Cloudflare 或 App Service 加 `img-src` 等 CSP header；要先確認 Google 登入的頭像（`lh3.googleusercontent.com`）、Swagger、Azure Blob 上的聽力題圖片（`jackyaiappstorage`）都列入，否則會破圖。
15. **幾個 master 既有功能多了上限（第 42 輪，D-158）**：AI 助理訊息 2 萬字、附件上傳每人每分鐘 20 次、匿名的 PDF 解鎖每個位址每分鐘 10 次、Word／Excel 附件與 /data-analysis 上傳的記憶體上限。都在程式常數裡（`ChatbotController.MaxMessageLength`、`RateLimitPolicies`、`AttachmentTextExtractor.MaxCharactersInPart`、`SessionUploadOptions.MaxReadBytes`），覺得太緊再調。另外兩個沒修、要你決定的：Whisper 轉錄不論錄音長度都只算 1 單位（要不要給錄音加長度上限）、匿名上傳的 256 MB 共用預算可被一個人佔滿 2 小時（NEXT 第 0 項）。
16. **再兩個上限（第 43 輪，D-159）**：考試的錄音轉文字最多 4 MB、錄音 2 分鐘自動停（麥克風提示會寫）；/data-analysis 的匿名上傳每個人（登入者或位址）最多佔 128 MB 記憶體。要調整的話，前者改常數 `MaxAudioBytes`，後者用 app setting `DataAnalysis__SessionUpload__MaxBytesPerCaller`。
17. **Data Analysis 的自訂 SQL Server 資料源與 dashboard 分享多了限制（第 44 輪，D-163、D-164，master 既有功能）**：自訂資料源的伺服器必須是網路上的主機（`tcp:xxx.database.windows.net,1433` 這類；內網 IP、`localhost`、沒有點的機器名、named pipe 都會被拒，已存的這類資料源會顯示「設定被拒」——在 App Service 上它們本來就連不到）；新增／修改資料源每人每分鐘 20 次；「任何人」可看的 dashboard，widget 資料未登入每個位址每分鐘 120 次、登入者 600 次。合併後若你自己有存資料源，打開 /data-analysis 確認它還能讀。伺服器名稱在每次連線前會先解析，指到內網就拒絕；但 DNS rebinding（解析時是公開位址、driver 連線時變成內網）在程式裡擋不住，若要完全擋，只能在 App Service 加出站限制（VNet 整合＋NSG），要花錢、你決定。dashboard「限定分享」的回應原本附上成員的名字與 Google id，現在只回 email。留給你決定的：存了之後沒帳號的 email 會消失，所以仍看得出誰有註冊；要完全不洩漏得改成「邀請」制（新資料表，D-163）。
18. **第七次巡檢留給你決定的（第 47 輪，D-173～D-175，都是 master 既有）**：(a) 文字轉語音 `/api/audio` 多了上限：未登入每個位址每分鐘 30 次、登入者 120 次，快取最多 128 MB（原本沒有上限，一個迴圈就能吃滿記憶體、用完 Azure Speech 免費額度）；被擋下時字典頁的「播放」改用瀏覽器自己的聲音（D-176）。(b) **匿名查字典仍可無限產生新字**（每個新字一次 OpenAI）：你寫明這是刻意的，而且爬蟲看字頁也會產生，所以沒動。若想限制，建議只限「產生新字」每個位址每分鐘幾次（已在 DB 的字不限，SEO 不受影響），並把產生失敗記幾分鐘，避免 OpenAI 掛掉時每次查詢都重付。(c) 帳號連線的 token 換新現在一次一個（Atlassian 同時換兩次會斷線）；OAuth 回來的網址只寫站內認得的 provider 名稱。
19. **MCP Apps 的實測（第 54 輪，D-203～D-205）**：合併後在 production 試一次「別人的 MCP App」：本機 `npx -y @modelcontextprotocol/server-basic-vanillajs` ＋ `cloudflared tunnel --url http://localhost:3001`（免費、免帳號的 quick tunnel），把 `https://<tunnel>/mcp` 加到 /connectors，Studio 問「現在 server 那邊幾點？」。或直接試公開的 `https://mcpplaygroundonline.com/mcp-app-server`（開發環境連不到，沒驗證它的 app 是不是用 `_meta.ui` 宣告）。本站現在會在 `initialize` 宣告 `io.modelcontextprotocol/ui`，若某個 server 因此行為異常，回報給我（`McpAppsCapability` 一行就能拿掉）。
20. **AI 用量的新規則與兩個之後的建議（第 63 輪，D-226、D-231）**：(a) Studio AI 與資料分析助理的一則訊息若要第 4 次以上的工具請求，每次多收 1 單位（`UsageFeature.ChatToolRound`，一則最多多 5；額度不夠時用已查到的結果回答，不報錯）。若覺得太嚴，改 `UsageCosts.FreeChatToolRounds`。(b) 之後可做：connector 的工具清單目前只在新增、測試連線、OAuth 回來、更新認證時重新讀；別人的 MCP server 加了新工具，讀者要按「測試連線」才看得到（design/03 說每小時，凍結前沒做）。
21. ~~換 token 加密 key 之前（第 64 輪，D-235）~~ **第 65 輪已處理（D-238）**：「更新認證」表單對 API key 多了選填的 header 名稱欄位，讀不到原本的 header 時伺服器會要求填上，不必移除重加。輪換 key 時仍要提醒使用者：所有 connector 與帳號連線都要重新輸入認證或重新授權。
22. **Data Analysis 自訂資料源多了上限（第 66 輪，D-244，master 既有功能）**：每人最多 20 個資料源；查詢端點每人每分鐘 300 次（未登入每個位址 120）；SQL Server 連線交給 driver 前夾住 `Min Pool Size` 0、`Max Pool Size` ≤ 5、`Command Timeout` ≤ 120 秒（0 也變 120）、重試 ≤ 2 次——**若你自己的資料源有超過 2 分鐘的查詢會被中斷**，需要的話調 `DataSourceConfigValidator.MaxCommandTimeoutSeconds`。另外修了編輯資料源時「不重打密碼就改伺服器／資料庫」會被默默丟掉的 bug。ADX 的 Tenant ID／Client ID 不能有空白、`;`、`=`、引號。
