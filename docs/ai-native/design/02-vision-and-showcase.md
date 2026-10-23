# 02 — 願景與成果發表故事線

## 一句話

**Studio AI：一個在每一頁都叫得出來的 AI 助理，會用本站所有功能，也能接上你在外面的世界（任何 remote MCP server），而且動手之前一定先問你。**

## 為什麼這跟上了 2026 的趨勢

| 趨勢 | 我們怎麼做 |
|---|---|
| MCP 成為 AI 接工具的標準（2026-07-28 stateless 規格、OAuth 2.1 + PRM + CIMD） | 使用者貼一個 MCP URL 就能用；需要登入的，自動跑完規格裡的探索與授權流程 |
| Agent 會「動手」，所以要 human-in-the-loop | 讀取自動、寫入出確認卡（看得到確切參數），每個工具可設「自動／需確認／停用」 |
| Generative UI／MCP Apps | 工具結果直接畫成圖表、表格、單字卡、dashboard 預覽卡，不是一坨文字 |
| 「網站本身就是工具」 | 本站功能（字典、單字庫、Portfolio、Data Analysis、Dashboard）都包成 AI 工具，同一套介面 |
| 同一個 AI 到處都在 | 一套共用 chat 元件：/finance、/data-analysis、全站助理（⌘K）都是它，只換 profile |

## 成果（Jacky 11/5 驗收時看到的）

1. **全站助理（⌘K / 右下角按鈕）**：任何頁面叫出來，知道你在哪一頁。
   - 「把 serendipity 查一下，加進我的單字庫」→ 單字卡 → 確認卡「加入單字庫：serendipity」→ 按「允許」→ 完成，附連結。
   - 「我的投資組合現在怎麼樣？」→ Portfolio 摘要卡（淨值、報酬、配置）。
2. **Data Analysis 最上方的 AI 對話**：「去年各區每月營收趨勢，畫成折線圖」→ 模型自己看 schema、查 distinct 值、跑 summarize → 對話裡出現**互動圖表** → 「存到新的 dashboard『營收週報』」→ 確認卡 → 完成，附 dashboard 連結。下方原本的「問 AI」不變。
3. **Connectors（/connectors 演進版）**：
   - 目錄：本站工具（內建）、台股資料（tw-stock MCP）、Google／Microsoft／Atlassian（原有 OAuth 連線，照常可用）、精選的公開 MCP server。
   - 「新增 MCP server」：貼 URL → 自動偵測需不需要登入 → 免認證／API key／Bearer／OAuth（PRM 探索 + CIMD／DCR／手填 client）→ 測試連線 → 列出工具，逐一設權限。
   - 加好的 connector 立刻出現在每個 AI 對話的工具裡。
4. **Stock Intelligence**：行為不變（同一套共用 chat 元件），使用者看不出差別，但程式碼與 Data Analysis、全站助理共用。
5. **給開發者**：新增一個 connector = 一份描述（remote MCP）或實作一個介面（原生 connector），附範例與測試樣板（`CONTRIBUTING-CONNECTORS.md`）。

## 11/5 demo 腳本（草稿，階段 2 定稿並補截圖）

前置：本機或 production 已設定 `Settings:AnthropicApiKey`；以一般帳號登入。

1. **開場（30 秒）**：首頁 → 按 ⌘K → 全站助理打開，顯示「我能幫你做什麼」與目前頁面。
2. **本站即工具（1 分鐘）**：輸入「查 serendipity，順便加入我的單字庫」→ 看到工具呼叫進度 → 單字卡 → 確認卡 → 允許 → 「已加入」+ 連到 /english/repository。
3. **資料 → 圖表 → dashboard（2 分鐘）**：到 /data-analysis → 最上方對話框問趨勢 → 互動圖 → 「存成 dashboard」→ 確認 → 打開 dashboard 看到同一張圖。
4. **接上外面的世界（2 分鐘）**：/connectors → 新增 MCP server → 貼一個公開 MCP URL（例：Microsoft Learn MCP）→ 測試連線，列出工具 → 回到助理問一個只有那個 server 答得出來的問題 → 回答附來源。
5. **安全（1 分鐘）**：示範一個寫入工具被擋下來要確認；把某工具設成「停用」後，AI 說它沒有這個能力；connector 頁顯示權限與最後錯誤。
6. **Stock Intelligence（30 秒）**：/finance 問一檔股票，跟以前一樣好用。
7. **收尾**：架構圖（`SHOWCASE.md`）與「新增一個 connector 要寫多少 code」。

## 「驚艷」檢查表（每個里程碑完成時回頭對照）

- 第一眼：叫出助理的動畫、工具執行中的即時進度（哪個 connector、哪個工具、花多久）。
- 結果是 UI 不是文字：圖表、表格、卡片、可點的連結。
- 確認卡讓人安心：清楚、可撤回、看得到參數。
- 新增 MCP server 一分鐘內完成。
- 任何失敗都有人話：沒設定 key、connector 斷線、超過額度。
