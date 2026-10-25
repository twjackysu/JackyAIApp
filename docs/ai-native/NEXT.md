# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1–M4 與延伸（本站 MCP server、OAuth、MCP App）都完成；第 10 輪加了跨訊息 taint、AI 今日簡報、語音輸入、MCP App 按鈕；第 11 輪加了接著問、圖表卡片換圖、「帶我去」；第 12 輪加了「存完打開」、dashboard 的「AI 解讀」（`read_dashboard`）、確認卡精簡、手機 dashboard 一欄；第 13 輪加了 ⌘K 指令列（`quickActions`）與 /finance 工具中文名稱（`toolLabels`）；第 14 輪加了指令列打開 dashboard、字典頁與 /dashboards 的 Studio 建議、**複習小測驗**（`review_quiz`＋可作答的 quiz 卡片）。第 15 輪加了 **`saved_words`**（用自己的單字寫短文，單字庫頁的第一個建議）與回答的 **「朗讀」**（`useSpeechOutput`，英中分聲音）。第 16 輪加了 **`update_holding`**（用說的或貼券商成交回報截圖記一筆交易，確認後更新投資組合；/portfolio 的建議「幫我記一筆今天的交易」）。第 17 輪加了 **選取文字問 Studio AI**（`SelectionBar`：解釋、翻譯、問 AI、查字典、加入單字庫；頁面文字帶 `quotesPageText` 當外部文字）。第 19 輪加了 **一句話做出整個 dashboard**（/data-analysis 的 prompt＋建議）與確認卡的 **全部允許／全部拒絕**（只限本站工具、未讀外部內容）。11/4 起功能凍結；在那之前還有很多輪，可以再加「少而精」的驚艷點，但每一個都要能 demo、有測試。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`；紅的先修（annotation 看法見排程指示）。被後一次 push 取消（cancelled）是正常的，看最新一次。
- **後端改動先跑離線型別檢查**（D-021）：`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`，然後 `CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh origin/feature/ai-native`（**以 branch 為基準**，只列這輪新出現的診斷；以 master 為基準會列出 30 多個已知雜訊）。已知雜訊：EF 擴充方法（`AsNoTracking`、`FirstOrDefaultAsync`…）、`SaveChangesAsync` 的 CS1503、`[GeneratedRegex]` 的 CS8795、`using var db = new AzureSQLDBContext` 的 CS1674。
- **畫面驗證**：`npm run build` 後用 playwright（`NODE_PATH=$(npm root -g)`）`page.route` 從 `dist/` 回檔案、`/api/*` 回假資料；SSE 用 `route.fulfill({ contentType: 'text/event-stream', body })`，事件 `start`／`tool_use`／`tool_result`（含 `card`）／`delta`／`done`。第 6 輪的兩個腳本樣子：/connectors（`/api/connectors/catalog` 回 `{ data: { entries, installed, customLimit } }`、`/api/connectors/status` 回 `[]`、probe 回 `{ data: {...} }`），Studio（首頁 → `Control+k` → 填 textbox → Enter）。

## 1. CI 狀態

- 第 19 輪：開工時最新 CI 紅（Jacky 改名 `QuickActionChips.tsx` 沒改 import），已修；本輪最後一次含程式碼的 push 40176286605（審查修正）綠。截圖 `selection.cjs` 多了情境 D（/data-analysis 回答裡選取）、E（一句話做出整個 dashboard 的確認卡），`ONLY=D|E`。
- **Jacky 在旅行中仍可能直接 push 到這個 branch**（25c21dd）：開工先看 `git log` 有沒有不是迴圈的 commit，看懂再接著做，不要蓋掉。
- clone 可能是 shallow：開工先 `git fetch --unshallow origin`（否則 merge-base 與 `rev-list --count` 不對）。
- 截圖：`.loop/shots/chat.cjs`（Studio／指令列／測驗／短文＋朗讀／記交易、貼截圖記交易／Data Analysis／今日簡報／語音／帶我去／換圖＋接著問／dashboard 的 AI 解讀／/finance，桌機＋手機）、**`selection.cjs`**（第 17 輪：字典頁選例句→翻成中文；Studio 短文裡選字；選取用 `selectIn(page, locator, text, last)` 在頁面內建 Range）、`connectors.cjs`、`mcp-app.cjs`。只想拍一個情境：用 python 取 chat.cjs 開頭到 `(async () => {` 之前（dist 與 fixtures 路徑改成絕對路徑），加上只含那一段的 IIFE，另存在 scratch 跑。
- **前端改動一定要跑 `npm run build`**（vitest 不做型別檢查），**改了 `SitePages.cs` 也要跑前端測試**（`quickActions.test.ts` 讀它）。`npm run lint`＋`npm run test` 加起來約 6 分鐘，放背景跑再看 log。`npx tsc -b` 會改 `tsconfig.node.tsbuildinfo`，commit 前 `git checkout` 它。**regex 裡的 `\uXXXX` 用 Write 工具寫會變成真的字元**，寫完 grep 確認（或用 python 寫）。
- playwright 的手機 viewport 仍是滑鼠環境：回答下方的複製／朗讀只在 hover 時出現（`MessageActions`）；選取工具列在觸控螢幕會放在選取下方，截圖裡看到的是滑鼠版（上方）。

## 2. 下一步（依序）

1. **選做（少而精，11/3 前）**——挑一個做完、能 demo、有測試：
   - 「一句話做出整個 dashboard」在 Studio AI 裡也能用？（Studio 沒有 `ask_data`；評估是否值得，或只在 /dashboards 的 Studio 建議導去 /data-analysis。）
   - /data-analysis 的「問 AI」（下方）的回答與查詢結果表的選取巡檢（需要假的 sources 與 summarize API，截圖腳本仿 `selection.cjs` 情境 D）。
   - 上傳檔案（session）時，/data-analysis 的「做一整個 dashboard」建議隱藏或換字（現在只靠 prompt 在第一張後停下，D-093）——`AiChatConfig.suggestions` 是靜態的，要看 `AiAssistantLauncher` 能否依頁面狀態換。
   - 新增本站寫入工具時，記得在 `refreshAfterTool` 補上它改了哪些 tag（D-085）。
2. **SHOWCASE 定稿**（11/4–11/5）：最終統計（輪數、commit 數：`git rev-list --count --no-merges origin/master..HEAD`）、demo 腳本走一遍、合併檢查清單再對一次 migration 清單（`git diff --stat origin/master -- JackyAIApp.Server/Data/Migrations`；第 14–19 輪沒有新 migration）。

## 3. 已經做好的零件

- 後端：`SafeHttp`、`OutboundAddressPolicy`、`McpServerUrl`、`ToolNames`、`IChatProfile`／`ChatTurnRequest`、agent loop（`ClientToolRunner`：確認、權限、taint、卡片守門）、`ChatApprovalRegistry`、`ChatConversations.ReadOutsideText`（跨訊息 taint）、`ConnectorInstallations`＋`ConnectorCatalogService`／`ConnectorInstallService`／`ConnectorToolSource`／`ConnectorSecrets`／`McpToolClient`、`BuiltInMcpConnectors`（tw-stock、DeepWiki、Notion、Linear）、**MCP OAuth**（`McpOAuthDiscovery`、`McpOAuthFlow`、`McpOAuthTokens`、`McpOAuthEndpoints`、`McpOAuthUrls`、`McpOAuthCallbackController`）、`SiteToolSet`／`ISiteTool`、`StudioChatProfile`＋`OpenPageTool`、English 的 `WordTools`（含 `SavedWordsTool`、`IRepositoryService.GetRecentWordsAsync`）／`LearningProgressTool`／`ReviewQuizTool`（＋`IReviewQuizService`、`IDailyChallengeQuestionGenerator.GenerateFor`）、Portfolio 的 `PortfolioSummaryTool`／`UpdateHoldingTool`（`HoldingChange`）、Data Analysis 的 `AskDataTool`／`DescribeDataTool`／`SaveToDashboardTool`（含 `open`）＋`DataAnalysisCharts`、`ReadDashboardTool`（Studio 與本站 MCP server 用）、Connectors 的 `GoogleCalendarTool`（原生 connector）、`AiToolDescriptor.OutsideText`、step-up（`McpServerException.RequiredScope`）、`CachedTools.Changed`（rug pull）、**本站 MCP server**（`Services/Connectors/SiteMcp`：`SiteMcpServer`、`SiteAccessTokenService`、`SiteTokenAuthenticationHandler`、`SiteOAuthServer`／`SiteOAuthClients`／`SiteOAuthCodes`／`Pkce`；controllers `SiteMcpController`、`SiteAccessTokensController`、`SiteOAuthController`）。測試的假網路在 `TestHelpers`：`FakeMcpServer`、`FakeAuthorizationServer`、`FakeWeb`、`TestStateService`；本站工具的一致性由 `SiteToolDefinitionsTests` 檢查。
- 本站 MCP server 的 MCP App：`SiteMcpApp`（`ui://jacky-studio/card.html`；卡片按鈕 `IOffersAppActions`／`AddActions`），畫面用 `.loop/shots/mcp-app.cjs`（自己扮 host，含 `<img onerror>` 當資料）驗證；/connectors 用 `.loop/shots/connectors.cjs`。
- 前端：`components/aiChat`（`AiChatConfig`、`variant="sheet"`、ToolActivity、ApprovalCard〔含「以後直接執行」；等待時在文字之後〕、卡片、`hooks/useSpeechInput` 語音、`hooks/useSpeechOutput`＋`utils/speechText` 朗讀、`ask` prop〔含 `fresh`〕、`utils/followUps`＋`FollowUps`、`onNavigate`＋`sitePathOf`；`AiAssistantLauncher` 的 `onNavigate` 等回答正常結束）、`Dashboard/viewLayout`、dashboard 的「AI 解讀」（`READ_DASHBOARD_PROMPT`）、`hooks/chatStatus`、`StudioAssistant/askStudio`、`StudioAssistant/quickActions`＋`QuickActionChips`（面板的 `inputAccessory`；含讀者的 dashboard）、`StudioAssistant/QuizCardView`＋`quizCard`（卡片 renderer 的 `saved`）、`AiChatConfig.toolLabels`＋`withToolLabel`、`aiChat/hooks/useAskQueue`（面板 ask 的佇列）、`ApprovalCard` 的全部允許／全部拒絕、`aiChat/selection`：`SelectionBar`＋`readSelection`（`noSelectionAsk`）＋`selectionActions`（`selectionStepOf`；Studio 與 `AiAssistantLauncher` 共用，選取文字問 AI；`SendOptions.quotesPageText`、面板的 `inputQuotesPageText`）、`Portal/BriefingCard`、`Connectors/callbackNotice`、`components/StudioAssistant`、/connectors v2（含 OAuth 路徑）、/data-analysis 的 AI 對話、`Connectors/SiteMcpSection`（token 與設定）、`OAuthConsent`（/oauth/authorize）。

## 4. 之後

design/07：M4 → 11/3（TestKit、Google Calendar 原生範例），11/4–11/5 打磨與 SHOWCASE 定稿。延伸：MCP Apps、跨輪 taint（本站對外 MCP server 已在第 8 輪完成）。
