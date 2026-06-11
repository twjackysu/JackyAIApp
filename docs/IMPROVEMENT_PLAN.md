# Improvement Plan（2026-06-10 掃描結果）

> 本文件是給後續 coding session 的自包含執行計畫。每個 PR 獨立、可分開 merge。
> 完成一項就勾掉；全部完成後可刪除本文件。

## 背景

- 後端：.NET 10 Web API（`JackyAIApp.Server`），credit 扣點有兩種機制：
  - 宣告式：`[ConsumeCredits(CreditCosts.X, "reason", "desc")]` action filter（`Common/ConsumeCreditsAttribute.cs`），不足時回 403 + "Insufficient credits. Required: N."
  - 手動式：service 內呼叫 `ICreditService.ConsumeCreditsAsync(...)`
- 前端：React + TS + MUI v9 + RTK Query（`jackyaiapp.client`）。
  - 全域 `src/redux/errorMiddleware.ts` 攔截 RTK Query 的 403 + "Insufficient credits" → 自動開啟購買 credit 的 Modal（`openCreditModal`）。
  - Credit 費用顯示常數在 `src/constants/creditCosts.ts`（鏡像後端 `Common/CreditCosts.cs`）。
- 驗證指令：
  - 後端：`dotnet test JackyAIApp.Server.Tests/JackyAIApp.Server.Tests.csproj`（目前 200 tests 全綠）
  - 前端：在 `jackyaiapp.client/` 跑 `npx tsc --noEmit`、`npx eslint <changed files> --fix`、`npx vite build`

---

## PR 1（P0）：補計費漏洞 — 免費的 OpenAI 呼叫

### 1a. 造句測驗 AI 評分沒收費（與 UI 標示不符）

- 現況：`ExamController.cs` 的 `POST sentence/evaluate` 沒有 `[ConsumeCredits]`，
  `ExamService.EvaluateSentenceAsync` 內也不扣點 — 但它是完整 GPT 呼叫。
  前端 `SentenceTestCard` 的「提交答案」按鈕下方卻已標示「AI 評分消耗 2 credits」。
- 修法：在 `Common/CreditCosts.cs` 新增 `SentenceEvaluation = 2`，
  仿照 `GradeTranslationAsync`（`ExamService.cs` 約 72–80 行）的手動扣點模式，
  在 `EvaluateSentenceAsync` 的輸入驗證之後扣點（驗證失敗不應扣錢）。
  不足時回 `ServiceResult.Fail(ErrorCodes.Forbidden, $"Insufficient credits. Required: {CreditCosts.SentenceEvaluation}.")`
  ——務必用這個訊息格式，errorMiddleware 靠它觸發購買 Modal。
- 前端：`src/constants/creditCosts.ts` 新增 `SENTENCE_EVALUATION: 2`，
  `SentenceTestCard/index.tsx` 的「提交答案」caption 改用它（目前誤用 `SENTENCE_TEST`）。

### 1b. Whisper 語音轉文字免費

- 現況：`ExamController.cs` 的 `POST whisper/transcribe` 無扣點；
  翻譯測驗 + 對話測驗的麥克風每次錄音都打一次 OpenAI Whisper。
- 修法：`CreditCosts.cs` 新增 `AudioTranscription = 1`，
  在 `ExamService.TranscribeAudioAsync` 內手動扣點（同 1a 模式與訊息格式）。
- 前端：`creditCosts.ts` 新增 `AUDIO_TRANSCRIPTION: 1`；
  在兩處麥克風按鈕的 Tooltip 文字加上「（轉錄消耗 1 credit）」：
  - `src/Exam/TranslationTestCard/index.tsx`
  - `src/Exam/ConversationTestCard/components/MessageInput.tsx`

### 1c.（需決策）字典查詢與 TTS 是否開始收費

- 現況：`CreditCosts.DictionaryLookup = 1`、`TextToSpeech = 1` 已定義但從未掛上。
  `DictionaryController` / `AudioController` 完全免費。
- 若決定收費：直接在對應 action 掛 `[ConsumeCredits(CreditCosts.DictionaryLookup, "dictionary_lookup", "Dictionary word lookup")]`（TTS 同理），
  前端在查詢按鈕/發音按鈕附近標示費用。
- **決策點：使用者尚未拍板。實作前先問一句「字典 / TTS 要開始收費嗎？」**
  （註：字典查詢若命中 DB 既有單字可能不該收費——掛 attribute 是「每次 API 呼叫都收」，
  若只想對 AI 生成的 cache-miss 收費，需改成 service 內手動扣點。實作前先確認
  DictionaryController 的查詢流程是否有 DB cache。）

### 驗收

- [ ] 造句提交評分會扣 2 credits；credit 不足時前端跳購買 Modal
- [ ] 錄音轉文字會扣 1 credit
- [ ] 200 個既有測試不壞；若 ExamService 有單元測試，補上「不足扣點時不呼叫 OpenAI」的 case
- [ ] UI 標示與實際扣點一致

---

## PR 2（P1）：砍掉每頁都載的 930KB chunk

- 現況：`src/components/HtmlViewer/index.tsx` 頂部 `import hljs from 'highlight.js'`
  載入完整 highlight.js（所有語言）。HtmlViewer 被 `FloatingChatbot/components/PreviewDialog.tsx` 引用，
  FloatingChatbot 自從變成全域元件後，這個 ~930KB 的 chunk（`vite build` 輸出可見）每頁都載。
- 修法（兩段都做）：
  1. `highlight.js` 改為 core + 按需註冊：
     ```ts
     import hljs from 'highlight.js/lib/core';
     import xml from 'highlight.js/lib/languages/xml';      // html
     import javascript from 'highlight.js/lib/languages/javascript';
     import css from 'highlight.js/lib/languages/css';
     hljs.registerLanguage('xml', xml); // ...
     ```
     HtmlViewer 只 highlight HTML 內容，xml/javascript/css 三種就夠。
  2. `PreviewDialog.tsx` 中 `HtmlViewer` 改 `React.lazy` + `<Suspense>`
     （preview 是點按鈕才開的 Modal，天生適合 lazy）。
- 注意：`MarkdownMessage.tsx` 用的是 `rehype-highlight`（自帶 lowlight common 子集，~323KB chunk），
  本 PR 不動它；若要進一步瘦身是另一個獨立題目。
- 驗收：
  - [ ] `npx vite build` 後 FloatingChatbot chunk 從 ~930KB 降到 ~200KB 以下
  - [ ] 聊天視窗開 HTML preview、切 code view，highlight 正常

---

## PR 3（P2）：Chatbot 402 接購買 Modal + 錄音 hook 去重

### 3a. Chatbot credit 不足時引導購買

- 現況：`ChatbotController.Chat` 在 credit 不足時回 402 + 純文字。
  chatbot 走原生 fetch（`src/hooks/useChatStreaming.ts`），不經過 RTK Query，
  所以全域 errorMiddleware 攔不到——使用者只在聊天泡泡看到一行錯誤字，沒有購買入口。
- 修法：`useChatStreaming.ts` 的 `!response.ok` 分支中，若 `response.status === 402`，
  dispatch `openCreditModal({ message, requiredCredits: 5 })`（`src/redux/uiSlice.ts`）。
  hook 內拿 dispatch 用 `useAppDispatch`（`src/redux/hooks.ts`）。
  仍保留把錯誤文字丟回聊天泡泡的既有行為。

### 3b. 錄音邏輯去重

- 現況：`src/Exam/ConversationTestCard/hooks/useVoiceRecording.ts` 是乾淨的 hook；
  `src/Exam/TranslationTestCard/index.tsx` 卻有 ~60 行 inline MediaRecorder 邏輯
  （含一個 exhaustive-deps eslint-disable）。
- 修法：把 `useVoiceRecording`（連同它依賴的 transcribe 呼叫）搬到 `src/hooks/useVoiceRecording.ts`，
  `ConversationTestCard` 改 import 新路徑（舊 `hooks/index.ts` 的 re-export 可一併清掉），
  `TranslationTestCard` 改用 hook、刪掉 inline 實作。
- 驗收：
  - [ ] chatbot 在 credit 不足時跳購買 Modal
  - [ ] 翻譯測驗與對話測驗的錄音→轉文字→填入輸入框行為不變
  - [ ] tsc / eslint / vite build 全綠

---

## PR 4（P3，可拆多個、不急）：結構性重構

### 4a. 拆 `TWSEOpenAPIService`（806 行，後端最大非 migration 檔）

- 兩個職責：①TWSE OpenAPI 抓資料（10 個 `GetXxxAsync` + JSON filter/format helpers）
  ②股票代碼解析（`ResolveStockCodeAsync` + 公司清單快取 + AI fallback，約 487–800 行）。
- 拆出 `Services/Finance/StockCodeResolver.cs`（介面 `IStockCodeResolver`），
  把 `ResolveStockCodeAsync / IsValidStockCode / GetCompanyListAsync / ParseCompanyList /
  ExtractCompanyInfo / FindExactCompanyMatchAsync / ResolveWithAIAsync / ContainsKeywords / CompanyInfo`
  全部搬過去。原 service 不再依賴 `IOpenAIService`。
  DI 註冊處（`Program.cs` 或 extension）加新服務；呼叫端跟著改建構子。

### 4b. `ChatbotController`（307 行）抽出 Dify client

- 現況混了三件事：SSE 串流代理、Dify conversations CRUD 代理、DTO class 定義在檔案底部。
- 拆法：
  - 新增 `Services/Dify/IDifyApiClient.cs` + 實作：統一持有 base url、API key header、
    封裝 `SendChatStreamAsync / GetConversationsAsync / GetMessagesAsync / DeleteConversationAsync`。
  - `ChatRequest / ChatConversationDto / ChatMessageDto` 移到 `DTO/`（既有 `DTO/DifyDto.cs` 旁）。
  - Controller 剩 HTTP 關注點（status code、SSE headers、credit 扣退）約 150 行。
- 注意：credit 扣點/退費邏輯（`ConsumeCreditsAsync` + `RefundChatMessageAsync`）留在 controller，
  不要搬進 client——client 只管 Dify HTTP。

### 4c. `useChatStreaming.ts` 的 260 行 switch 改 handler map

- 現況：單一 useCallback 內 switch 處理 9 種 Dify SSE event。
- 拆法：抽 `src/hooks/difyEventHandlers.ts`，
  `type DifyEventContext = { setStatus, appendContent, replaceContent, complete, fail }`，
  `const handlers: Record<string, (data: DifyEvent, ctx: DifyEventContext) => void>`。
  主迴圈變成 `handlers[data.event]?.(data, ctx) ?? defaultHandler(data, ctx)`。
- 這是純重構，行為不得改變；特別注意 `message_end` 的 500ms delay + early return、
  `agent_log` 的 output / llm_response 兩種取值路徑。

### 4d.（順手）`ScenarioSelector.tsx`（337 行）抽資料

- `SCENARIO_TEMPLATES` 常數陣列（~60 行純資料）移到同目錄 `scenarioTemplates.ts`，
  元件檔縮到純 UI。

---

## 已確認不是問題（不用動）

- 後端各 controller/service 大小在重構後已健康（DataAnalysis、Finance 已拆過）。
- `errorMiddleware.ts` 用 regex 從錯誤訊息抓 `Required: N` 雖不優雅，但兩端格式已耦合多處，
  改造收益低，維持現狀。
- Dify conversation history 走 proxy 不開 DB table 的決策維持不變。
