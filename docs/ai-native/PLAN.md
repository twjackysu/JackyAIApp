# AI 原生化計畫（feature/ai-native）

> 業主 Jacky 2026-10-24 ～ 11-05 不在，由排程迴圈（每 2 小時一輪）開發。repo 是唯一的記憶：每輪先讀 `NEXT.md`。

## 狀態

- 設計：**設計定稿**（2026-10-24 04:10，經一次對抗式評審並修正；之後的調整記在 DECISIONS.md）。
- 階段：1（開發）。M1 完成；**M2 垂直切片完成（10/24，提早）**：agent loop、同請求確認、工具權限與 taint、Data Analysis 助理、connector 目錄與自己的 MCP server（免認證／API key／Bearer）、/connectors 回到 portal。M3 已開始：Studio AI（⌘K 全站助理、open_page、English 的查字典／加入單字庫）完成；剩 MCP OAuth、Portfolio 工具。所有後端變更都由 branch CI 驗證（綠）。

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

## 時程（台北時間）

| 階段／里程碑 | 日期 | 內容 |
|---|---|---|
| 階段 0 | → 10/25 | 研究、設計定稿 |
| M1 引擎與共用元件 | 定稿後 → 10/27 | golden test、SSE parser 修正、NuGet 與 agent loop spike、模組重整、migration、前端搬到 `components/aiChat` |
| M2 垂直切片 | → 10/29 | 工具框架、同請求確認、Data Analysis 工具與對話、connector 框架（None/ApiKey/Bearer）、SSRF 防護、/connectors v2 |
| M3 全站助理與 MCP OAuth | → 11/1 | ⌘K Studio AI、English/Portfolio/導覽工具、MCP OAuth（PRM/CIMD/DCR/PKCE/iss/refresh/step-up） |
| M4 擴充與貢獻 | → 11/3 | Google Calendar 原生範例、CONTRIBUTING + TestKit、CLAUDE.md；延伸：MCP Apps |
| 階段 2 打磨 | 11/4 → 11/5 中午 | 凍結、修 bug、SHOWCASE |
