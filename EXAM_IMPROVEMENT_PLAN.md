# English Learning 測驗改善計畫

> 目標:針對現有四種測驗(克漏字/翻譯/情境對話/造句)提出 UX 與學習成效改善。
> 只改善現有功能,不新增大型功能。供後續實作參考(預計由 Sonnet 執行)。
> 撰寫日期:2026-07-03

## 現況盤點

| 測驗 | 前端 | 後端 | 現況評價 |
|---|---|---|---|
| 克漏字 Cloze | `jackyaiapp.client/src/Exam/ClozeTestCard/index.tsx` | `Generators/ClozeExamGenerator.cs` | 流程可用,但答錯無任何解釋、可暴力重試 |
| 翻譯 Translation | `jackyaiapp.client/src/Exam/TranslationTestCard/index.tsx` | `Generators/TranslationExamGenerator.cs` + `GradeTranslationAsync` | **問題最多**,流程卡、回饋弱 |
| 情境對話 Conversation | `jackyaiapp.client/src/Exam/ConversationTestCard/` | `ExamService.StartConversationAsync/RespondToConversationAsync` | 架構最好,但糾正會消失、無總結 |
| 造句 Sentence | `jackyaiapp.client/src/Exam/SentenceTestCard/index.tsx` | `Generators/SentenceExamGenerator.cs` + `EvaluateSentenceAsync` | 四者中回饋最完整,小修即可 |

共用基礎已存在、可直接利用:
- SM-2 SRS:`Services/SpacedRepetition/SpacedRepetitionService.cs` + `POST /api/exam/record-result`(免費)
- Review 流程已有 XP / 結果畫面模式可參考:`src/Review/index.tsx`
- TTS 基礎設施已存在(Dictionary 發音)

---

## P0 — 高價值、低成本(先做)

### P0-1 翻譯測驗:流程重整(最優先)

檔案:`src/Exam/TranslationTestCard/index.tsx`

現有問題:
1. **流程死結**:AI 批改完成後,`Next` 按鈕條件是 `showAnswer || !feedback` → 批改後 Next 消失,必須再點 `Show Answer` 才會出現。使用者會卡住。
2. 輸入框是單行小 TextField,翻譯整句很痛苦;無 Enter 送出。
3. 「翻譯評級」標題 + tooltip 在還沒作答前就常駐畫面,干擾。
4. 批改後參考答案不會自動出現,要另外點按鈕。
5. 批改後 Submit 被禁用(`disabled={!!feedback}`),無法修正重交。

改法 — 改成明確三階段狀態機 `answering → graded → (next)`:
- `answering`:題目(中文句)+ 多行 fullWidth TextField(Enter 送出、Shift+Enter 換行)+ 錄音鈕 + Submit + Show Answer(直接跳 graded 但標記未作答,SRS 記 incorrect 或不記)。
- `graded`:自動同時顯示 (a) 使用者的翻譯 (b) 參考答案 `data.english` (c) 評級與理由 (d) SRS note。移除獨立 Show Answer 按鈕。提供「修改重交」(再次批改,再扣批改 credit,UI 明示)與「下一題」。
- 評級說明 tooltip 移到 graded 階段的評級 chip 上。
- 介面文字統一繁中(`Submit`→`送出`、`Show Answer`→`直接看答案`、`Next`→`下一題`)。

### P0-2 翻譯測驗:結構化批改回饋(後端)

檔案:`Prompt/Exam/TranslationQualityGradingSystem.txt`、`ExamService.GradeTranslationAsync`、DTO、前端 types

現有問題:
- 回傳只有一個字串(例:`A級 原因:...`),前端用 `grading.match(/[A-E]/)` 抓等級 — 理由文字中任何 A–E 字母都可能誤判,且無法分區呈現。

改法 — 批改回傳結構化 JSON:
```json
{
  "grade": "A|B|C|D|E",
  "reason": "簡短中文理由",
  "correctedTranslation": "修正後的英文(B 以下提供)",
  "wordUsed": true
}
```
- Prompt 同步改寫,加入 few-shot 範例。
- 前端依 grade 上色 chip(A/B 綠、C 黃、D/E 紅),`correctedTranslation` 單獨顯示,SRS 判定改用 `grade` 欄位(A/B = correct),移除 regex。
- `wordUsed`:告知使用者是否有用到目標單字(不影響評級,只提示)。

### P0-3 翻譯測驗:顯示目標單字

檔案:`src/Exam/TranslationTestCard/index.tsx`(`data.data.word` 已在 response 裡)

- 題目下方顯示「目標單字:xxx」chip(可先預設隱藏、點擊顯示,當提示用)。
- 理由:出題本來就是圍繞使用者的不熟單字,藏著只會讓使用者不知道為何被扣分,顯示才有練習目標。

### P0-4 測驗選擇頁:修正情境對話被誤鎖

檔案:`src/Exam/index.tsx`

現有問題:
- Repository 沒單字時,整頁被 `RepositoryNoWordAlert` 取代(line 49 三元判斷),四顆按鈕全部不見 → 不需要單字的「情境對話」也進不去。按鈕上原本的 `disabled={isRepositoryNoWord}` 變成 dead code。

改法:
- 移除頂層三元,永遠渲染四顆按鈕;沒單字時只 disable 三顆需要單字的,情境對話保持可點,alert 顯示在按鈕下方。

### P0-5 出題改吃 SRS 排程(學習成效最大單一改善)

檔案:`ExamService.GetRandomUserWordIdAsync`(`Services/Exam/ExamService.cs:357`)

現有問題:
- 出題單字是 UserWords 均勻隨機,完全忽略已存在的 SM-2 欄位(`NextReviewDate`、`ConsecutiveCorrect`),與 Review 功能脫節。

改法(不改 schema,純查詢邏輯):
1. 優先從「到期單字」(`NextReviewDate <= today`)隨機挑。
2. 沒有到期的,從 `ConsecutiveCorrect` 最低 / `ReviewCount` 最少的前 N 個中隨機挑(弱字優先)。
3. 都沒有才退回全體隨機。
- 三種單字測驗(Cloze/Translation/Sentence)自動受惠,`record-result` 已會回寫 SRS,形成閉環。

### P0-6 克漏字:答錯要有解釋

檔案:`src/Exam/ClozeTestCard/index.tsx`

現有問題:
- 答錯只顯示 `Incorrect, try again!`,可無限重試直到猜對,學不到東西。

改法(不需新 AI call):
1. 送出後直接判定:答對顯示綠、答錯顯示紅 + **正確答案** + 完整句子(答案填入 blank、目標字 highlight)。
2. 移除重試迴圈(SRS 本來就只記第一次,行為對齊)。
3. 附「查看單字」連結到 `/dictionary?word=xxx`(或彈出 WordCard),強化學習。
4. 介面文字統一繁中;選項支援鍵盤 1–4 選、Enter 送出。
5. 快取題目每次進場前端重新洗牌選項順序(存 DB 的順序固定,重複練習會背位置)。

---

## P1 — 高價值、中成本

### P1-1 情境對話:糾正記錄持久化

檔案:`src/Exam/ConversationTestCard/index.tsx`、`components/ChatMessages.tsx`、`components/CorrectionAlert.tsx`

現有問題:
- `correction` 是單一 state,每輪覆蓋 — 上一輪的文法糾正直接消失,對話結束什麼都沒留下。

改法:
- 把 correction 附掛在對應的 user turn 上(`ConversationTurn` 前端型別加 `correction?`),在該訊息泡泡下方顯示小型糾正條,永久保留在聊天串裡(語言學習 app 標準做法)。

### P1-2 情境對話:結束總結報告

檔案:前端同上 + `ExamService` 新 endpoint(或重用既有 completion pattern)

- 「開始新對話」旁加「結束並看總結」:把整段 history 丟給 AI,產出 (a) 本次糾正回顧 (b) 更道地的說法 2–3 個 (c) 值得加入 Repository 的單字建議。
- 一次 AI call,收尾有回顧感,學習價值高。建議消耗 credits 與 CONVERSATION_RESPONSE 同級。

### P1-3 情境對話:AI 訊息 TTS 播放

檔案:`components/ChatMessages.tsx`,重用 Dictionary 既有 TTS API

- 每則 AI 訊息加喇叭 icon,點擊播放(明示 credit 消耗,與現有 TTS 計價一致)。
- 聽力練習補上,情境對話從純讀寫變聽說讀寫。

### P1-4 造句測驗:範例答案防偷看

檔案:`src/Exam/SentenceTestCard/index.tsx`

現有問題:
- 「顯示範例答案」在作答前就可點,看完照抄還能拿高分 + SRS 記 correct,汙染排程。

改法(擇一,建議 a):
- (a) 範例答案移到評分後才可看(按鈕保留位置,未評分時 disabled + tooltip 說明)。
- (b) 看過範例才提交的,SRS 不記錄。

### P1-5 單字測驗共通:答題後的單字卡回饋

檔案:三個 TestCard + 可重用 `components/WordCard`

- 答完(graded/revealed)後顯示目標單字的精簡卡:KK 音標、發音鈕、1–2 個定義(資料 Repository 已有,免 AI call)。
- 把「考完就走」變成「考完複習一眼」,記憶掛鉤更強。

---

## P2 — 之後再說(記錄想法,不在本輪)

- **回合制 session**:一輪 10 題 + 結束畫面(正確率/XP,重用 Review 的 result screen 模式),取代現在無限單題流。
- 翻譯/克漏字題目難度分級(CEFR 或 1–5),仿造句測驗。
- 情境對話任務目標與完成判定(如「成功點完餐」)。
- 克漏字題目附中文翻譯(需改 schema 與生成 prompt)。
- Dictionary 搜尋自動完成 / 拼字建議(Hunspell 後端已有)。

## 不建議做

- 大改考試型態或新增第五種測驗 — 現有四種已涵蓋讀寫聽說,先把體驗做順。
- 把 credits 提示從畫面拿掉 — 計費透明是刻意設計,但可以視覺降級(縮小、變淡),別跟主要 CTA 搶注意力。

## 建議實作順序

1. P0-1 + P0-2 + P0-3(翻譯測驗一次改完,前後端一起動)
2. P0-4(選擇頁小修)
3. P0-5(SRS 出題,純後端)
4. P0-6(克漏字)
5. P1 依序

## 驗收重點

- 翻譯:批改後一畫面同時看到「我的翻譯 / 參考答案 / 評級+理由」,Next 永遠可按。
- 翻譯:regex `/[A-E]/` 移除,SRS 判定走結構化 grade。
- Repository 清空時,情境對話仍可進入。
- 有到期單字時,連續出題應明顯偏向到期字(可用 log 驗證)。
- 克漏字答錯:立即看到正解與完整句子,不再有 try again 迴圈。
