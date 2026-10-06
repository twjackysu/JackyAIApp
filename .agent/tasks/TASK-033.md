# TASK-033：單字頁內部連結（同義／反義／相關字可點擊，#407 SEO）

## Owner
Frontend Engineer

## Status
PR_OPENED

## Priority
MEDIUM（Requirement：Growth & Retention／#407 單字頁內部連結；PM 建議 MEDIUM）

## Objective
單字頁的 synonyms／antonyms／relatedWords 目前是純文字（`components/WordCard.tsx`），Googlebot 無法沿連結發現其他單字頁。改為真正的 `<a href>` 內部連結，加速收錄，同時讓使用者可連續查字（留存）。

## Requirements
1. `WordCard.tsx`：synonyms／antonyms／relatedWords 每個詞渲染為 React Router `Link`（輸出真實 `<a href="/english/dictionary/{word}">`，詞需 `encodeURIComponent`），維持原有排版與逗號分隔視覺。
2. 僅對單一英文單字／簡單詞組產生連結；空字串、含奇怪字元者維持純文字。
3. 點擊後導向該單字頁並捲回頁首；不得重複觸發既有的搜尋歷史／收藏副作用（沿用現有路由載入流程）。
4. 目標單字尚未收錄時，現有「查詢並快取」流程需正常運作（不新增後端端點、不為了連結預先呼叫 AI）。
5. 不改後端、不改 sitemap、不改 TASK-020 的 meta 注入。

## Acceptance Criteria
- 單字頁 DOM 中上述詞彙為 `<a>`，`href` 正確。
- 點擊連結可連續查字，瀏覽器上一頁可回到前一個單字。
- 未收錄單字點擊後正常顯示結果或明確錯誤（TASK-031 的不得無聲失敗原則）。
- `npm run lint`、`npm run test`（含 WordCard 連結的單元測試）、`npm run build` 全過。

## Technical Notes
- 無 AI 成本增加；連結僅是路由導覽。
- 風險：點擊未收錄字會觸發一次 AI 查詢（既有用量限制 `[UsageLimit]` 仍適用，匿名訪客遇 429 要顯示既有提示）。若 Boss 關心成本，可日後改為只連結「已快取」字，目前判斷不需要。
- 每日單字／首頁到單字頁的連結列為 Future Work，待本任務成效後再評估。

## Dependencies
無。開工前先在 master 推 `Status: IN_PROGRESS`。

## Result
- `WordCard` 的 synonyms／antonyms／relatedWords 改用新元件 `WordLinkList`（MUI `Link` + React Router `Link`，真實 `<a href="/english/dictionary/{encodeURIComponent(word)}">`）。
- `wordLinks.ts#isLinkableWord`：僅字母（含重音）與單一空白／連字號／撇號組成的單字或簡單詞組才連結，其餘維持純文字。
- `Dictionary/index.tsx`：word 變更時 `window.scrollTo(0, 0)`；其餘載入流程不變，未改後端／sitemap／meta 注入。
- 測試：新增 `WordCard.test.tsx`；lint、test（300 passed）、build 全過。

## Review
