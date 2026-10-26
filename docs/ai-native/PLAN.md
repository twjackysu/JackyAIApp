# AI 原生化計畫（feature/ai-native）

> 業主 Jacky 2026-10-24 ～ 11-05 不在，由排程迴圈（每 2 小時一輪）開發。repo 是唯一的記憶：每輪先讀 `NEXT.md`。

## 狀態

- 設計：**設計定稿**（2026-10-24 04:10，經一次對抗式評審並修正；之後的調整記在 DECISIONS.md）。
- 階段：1（開發）。M1、M2 完成；**M3 大致完成（10/24，提早）**：Studio AI（⌘K）、本站工具（字典、單字庫、學習進度、投資組合、open_page）、Data Analysis 用說的存 dashboard、**MCP OAuth**（PRM／AS metadata／DCR／CIMD／手填、PKCE、resource、iss、refresh）、目錄的 Notion／Linear。**M4 必要項目完成（10/24 第 7 輪）**：TestKit（假 MCP server／授權伺服器、完整 OAuth 整合測試、本站工具一致性測試）、Google 日曆原生 connector、403 step-up、確認卡「以後直接執行」、rug pull 防護。**M4 延伸（10/24 第 8 輪）**：本站對外的 MCP server（PAT＋OAuth 授權伺服器與同意頁，Claude.ai／Claude Code／Cursor 可用本站工具）。**第 9 輪**：本站 MCP server 成為 **MCP App**（Claude／ChatGPT 裡畫出本站的表格與連結卡片）、OAuth 端點限流、「經 OAuth 登入」標示、serverInfo 圖示、Studio 引導。**第 10 輪**：跨訊息 taint、首頁「AI 今日簡報」、語音輸入、MCP App 卡片按鈕、畫面巡檢修正。**第 11 輪**：今日簡報開新對話、接著問、圖表卡片換圖、「帶我去」。**第 12 輪**：「存完打開」、dashboard 的「AI 解讀」（`read_dashboard`）、確認卡精簡、手機 dashboard 一欄。**第 13 輪**：⌘K 指令列（打頁面名稱或英文單字直接去，不經 AI）、/finance 工具 chip 中文、/connectors 抽屜修正。**第 14 輪**：指令列打開 dashboard、頁面建議、複習小測驗（可作答的 quiz 卡片）。**第 15 輪**：`saved_words`（用自己的單字寫短文）、回答的「朗讀」。**第 16 輪**：`update_holding`（用說的或貼成交回報截圖記一筆交易，確認後更新投資組合）。**第 17 輪**：選取文字問 Studio AI（解釋、翻譯、查字典、加入單字庫；頁面文字當外部文字）。**第 18 輪**：選取工具列擴到 /finance 與 /data-analysis 的助理、作答中的題目不給。**第 19 輪**：一句話做出整個 dashboard、確認卡全部允許。**第 20 輪**：AI 之間交棒（Studio → /data-analysis、/finance 的助理）、持股的外資動向交給 Stock Intelligence。**第 21–23 輪**：Studio AI 的記憶、確認卡打磨。**第 24 輪**：本站 MCP server 的現成指令（MCP prompts）、Studio 的「/」現成指令。**第 25–27 輪**：「/」跟著頁面走、app 助理的「/」、找出我的弱點（`weak_spots`）。**第 28 輪**：語音對話（用說的問、自動朗讀；今日簡報「用聽的」）。**第 29 輪**：/connectors 搜尋 MCP 官方目錄（registry.modelcontextprotocol.io）。**第 30–31 輪**：Studio AI 幫你找 MCP server（`find_mcp_server`）、接上就「在 Studio AI 試試看」、加過的標「已加入」。**第 32–33 輪**：Jira 原生 connector（讀票、留言〔第一個寫入外部服務的原生 connector〕）、Outlook 行事曆。**第 34 輪**：看得見 AI 在想什麼（三個助理都串流思考摘要，摺疊在回答上方）。SHOWCASE 初稿已寫。所有後端變更都由 branch CI 驗證（綠）。

## 目標

讓整個網站更 AI 化：**Studio AI**——一套共用的 AI chat（Finance、Data Analysis、全站 ⌘K 助理），會用本站的功能、也能用使用者接上的任何 remote MCP server，寫入動作先確認。詳見 [design/02-vision-and-showcase.md](design/02-vision-and-showcase.md)。

## 設計文件

1. [研究](design/01-research.md)
2. [願景與成果發表](design/02-vision-and-showcase.md)
3. [Connector 架構](design/03-connector-architecture.md)
4. [共用 AI chat](design/04-shared-ai-chat.md)
5. [UX](design/05-ux.md)
6. [安全](design/06-security.md)
7. [實作計畫](design/07-implementation-plan.md)
8. [Studio AI 的記憶](design/08-memory.md)（第 21 輪新增，D-100）

## 時程（台北時間）

| 階段／里程碑 | 日期 | 內容 |
|---|---|---|
| 階段 0 | → 10/25 | 研究、設計定稿 |
| M1 引擎與共用元件 | 定稿後 → 10/27 | golden test、SSE parser 修正、NuGet 與 agent loop spike、模組重整、migration、前端搬到 `components/aiChat` |
| M2 垂直切片 | → 10/29 | 工具框架、同請求確認、Data Analysis 工具與對話、connector 框架（None/ApiKey/Bearer）、SSRF 防護、/connectors v2 |
| M3 全站助理與 MCP OAuth | → 11/1 | ⌘K Studio AI、English/Portfolio/導覽工具、MCP OAuth（PRM/CIMD/DCR/PKCE/iss/refresh/step-up） |
| M4 擴充與貢獻 | → 11/3 | Google Calendar 原生範例、CONTRIBUTING + TestKit、CLAUDE.md；延伸：MCP Apps（第 9 輪完成，本站 MCP server 端） |
| 階段 2 打磨 | 11/4 → 11/5 中午 | 凍結、修 bug、SHOWCASE |
