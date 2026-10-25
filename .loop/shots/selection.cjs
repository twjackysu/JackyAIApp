// 選取文字問 AI 的截圖（第 17、18 輪；迴圈專用，合併前與 .loop/ 一起刪）：字典頁選例句 → 翻成中文；Studio 短文裡選一個字；
// /finance 助理的回答裡選一段 → 問 /finance 自己的助理（第 18 輪）。
// 用法：cd jackyaiapp.client && npm run build && NODE_PATH=$(npm root -g) node ../.loop/shots/selection.cjs <輸出目錄>（前半段與 chat.cjs 相同）
// /data-analysis 助理的回答裡選一段（第 19 輪，情境 D）。
// 一句話做出整個 dashboard（第 19 輪，情境 E）。
// 只拍一個情境：ONLY=A|B|C|D|E|F|G|H（F 也拍交棒來的訊息上方的「由 Studio AI 轉來」，第 21 輪）。
// chat 的串流由頁面內的 fetch 替身送出（addInitScript），可以停在確認卡上不結束，所以拍得到「等待確認」的畫面。
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const dist = path.resolve(__dirname, '../../jackyaiapp.client/dist');
const out = process.argv[2] || '/tmp/shots';
fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.json': 'application/json', '.woff2': 'font/woff2' };
const user = { id: 'u1', name: 'Jacky', email: 'jacky@example.com', isAdmin: false, drawdownBrakeEnabled: false };

// ── 各情境的 SSE 事件 ─────────────────────────────────────────────────────────────────────
const start = { type: 'start', conversationId: 'c1', userMessageId: 1 };
const done = { type: 'done', conversationId: 'c1', assistantMessageId: 2 };
const delta = (text) => ({ type: 'delta', text });

const studioWord = [
  start,
  { type: 'tool_use', id: 't1', name: 'look_up_word', displayName: '查字典' },
  {
    type: 'tool_result', id: 't1', name: 'look_up_word', ok: true, durationMs: 380,
    card: { kind: 'link', title: 'serendipity', description: '/ˌsɛrənˈdɪpəti/ n. 意外發現美好事物的運氣', href: '/english/dictionary/serendipity', actionLabel: '看完整解釋' },
  },
  delta('**serendipity** 是「意外發現美好事物的運氣」，例如：\n\n> Finding this café was pure serendipity.\n\n我幫你把它加入單字庫，請確認：'),
  {
    type: 'approval_required', pendingId: 'p1', tainted: false,
    calls: [{ toolUseId: 't2', connector: 'site', connectorName: 'Jacky Studio', tool: 'add_word_to_repository', displayName: '加入單字庫', effect: 'write', input: { word: 'serendipity' }, summary: '把「serendipity」加入你的單字庫' }],
  },
  // 不結束：停在確認卡。
];

const studioPortfolio = [
  start,
  { type: 'tool_use', id: 't1', name: 'portfolio_summary', displayName: '投資組合' },
  {
    type: 'tool_result', id: 't1', name: 'portfolio_summary', ok: true, durationMs: 910,
    card: {
      kind: 'table', title: '持倉（依未實現損益）', totalRows: null,
      columns: [
        { key: 'name', label: '持倉', numeric: false },
        { key: 'value', label: '市值', numeric: true },
        { key: 'pnl', label: '損益', numeric: true },
        { key: 'pct', label: '損益 %', numeric: true },
      ],
      rows: [
        { name: '台積電（2330）', value: 1245000, pnl: 412000, pct: '49.5%' },
        { name: '元大台灣50（0050）', value: 486000, pnl: 61200, pct: '14.4%' },
        { name: 'Vanguard S&P 500（VOO）', value: 352000, pnl: 28900, pct: '8.9%' },
        { name: '長榮（2603）', value: 98000, pnl: -21500, pct: '-18.0%' },
        { name: '元大台灣50反1（00632R）', value: 41000, pnl: -9800, pct: '-19.3%' },
      ],
    },
  },
  delta('你的投資組合目前**總市值約 222 萬**，未實現損益 +47 萬。\n\n虧最多的兩檔：\n\n1. **2603 長榮**：-18.0%，航運運價回落。\n2. **00632R**：-19.3%，反向 ETF 在多頭裡本來就會持續磨損，適合短期避險，不適合長抱。\n\n要我幫你看看曝險比例嗎？'),
  done,
];

// /portfolio：「我今天加碼 0050 兩張，均價 185」→ 更新持倉的確認卡（停在確認卡）
const studioTrade = [
  start,
  delta('好的，幫你把今天的加碼記進投資組合，請確認：'),
  {
    type: 'approval_required', pendingId: 'p1', tainted: false,
    calls: [{ toolUseId: 't1', connector: 'site', connectorName: 'Jacky Studio', tool: 'update_holding', displayName: '更新持倉', effect: 'write', input: { symbol: '0050', change: 2000, price: 185 }, summary: '「0050」加 2,000（成交價 185）（存成今天的快照）' }],
  },
];

// 同上，允許之後：已更新的連結卡、說明、接著問
const studioTradeDone = [
  ...studioTrade,
  { type: 'approval_resolved', pendingId: 'p1', decisions: [{ toolUseId: 't1', approve: true }] },
  { type: 'tool_use', id: 't1', name: 'update_holding', displayName: '更新持倉' },
  {
    type: 'tool_result', id: 't1', name: 'update_holding', ok: true, durationMs: 420,
    card: { kind: 'link', title: '投資組合', href: '/portfolio', description: '元大台灣50（0050）：5,000 → 7,000', actionLabel: '看投資組合' },
  },
  delta('\n\n記好了：**0050 從 5,000 股加到 7,000 股**，成本 900,000 → 1,270,000 元（平均成本約 181.4 元），存成今天的快照，之前的快照留在歷史裡。'),
  done,
];

// 附上成交回報截圖 →「讀到兩筆」→ 兩筆更新持倉一起出現在確認卡（停在確認卡）
const studioTradeImage = [
  start,
  delta('我從截圖讀到兩筆成交：\n\n| 代號 | 買賣 | 數量 | 均價 |\n|---|---|---|---|\n| 0050 元大台灣50 | 買進 | 2,000 股 | 185.00 |\n| 2330 台積電 | 賣出 | 500 股 | 1,450.00 |\n\n請逐筆確認，允許後幫你記進投資組合：'),
  {
    type: 'approval_required', pendingId: 'p1', tainted: false,
    calls: [
      { toolUseId: 't1', connector: 'site', connectorName: 'Jacky Studio', tool: 'update_holding', displayName: '更新持倉', effect: 'write', input: { symbol: '0050', change: 2000, price: 185 }, summary: '「0050」加 2,000（成交價 185）（存成今天的快照）' },
      { toolUseId: 't2', connector: 'site', connectorName: 'Jacky Studio', tool: 'update_holding', displayName: '更新持倉', effect: 'write', input: { symbol: '2330', change: -500 }, summary: '「2330」減 500（存成今天的快照）' },
    ],
  },
];

// /finance：hosted 的 tw-stock 工具（沒有顯示名稱、沒有卡片），回答後的接著問。
const financeTrend = [
  start,
  { type: 'tool_use', id: 't1', name: 'get_stock_history' },
  { type: 'tool_result', id: 't1', name: 'get_stock_history', ok: true, durationMs: 1200 },
  { type: 'tool_use', id: 't2', name: 'get_technical_indicators' },
  { type: 'tool_result', id: 't2', name: 'get_technical_indicators', ok: true, durationMs: 800 },
  delta('**台積電（2330）近一個月**收在 1,450 元，月漲 6.2%。\n\n- 均線：5 日 > 20 日 > 60 日，多頭排列。\n- RSI(14) 68，接近過熱但還沒到 70。\n- 量能：上漲時放量、回檔時縮量，籌碼穩定。\n\n短線偏多，但追高前可以等回測 20 日線（約 1,390 元）。'),
  done,
];

const dataChart = [
  start,
  { type: 'tool_use', id: 't1', name: 'ask_data', displayName: '查詢資料' },
  {
    type: 'tool_result', id: 't1', name: 'ask_data', ok: true, durationMs: 2400,
    card: {
      kind: 'chart', title: '各地區每月營收',
      data: {
        viz: 'line', headers: ['月份', '地區', '營收'], groupHints: ['月份', '地區'], temporalHints: ['月份'],
        rows: ['2026-05', '2026-06', '2026-07', '2026-08', '2026-09'].flatMap((m, i) => [
          { 月份: m, 地區: '北區', 營收: 820 + i * 60 },
          { 月份: m, 地區: '中區', 營收: 540 + i * 25 + (i % 2) * 40 },
          { 月份: m, 地區: '南區', 營收: 610 - i * 15 },
        ]),
        plan: { viewBy: ['月份', '地區'], measures: [{ field: '營收', aggregation: 'sum' }] },
      },
    },
  },
  delta('北區營收連續五個月成長（820 → 1,060），南區則緩步下滑。我把這張圖存到「業績追蹤」dashboard，存好就幫你打開，請確認：'),
  {
    type: 'approval_required', pendingId: 'p1', tainted: false,
    calls: [{ toolUseId: 't2', connector: 'site', connectorName: 'Jacky Studio', tool: 'save_to_dashboard', displayName: '存到 dashboard', effect: 'write', input: { chartId: 'chart-k3f9', dashboard: '業績追蹤', open: true }, summary: '把「各地區每月營收」存到 dashboard「業績追蹤」，存好後打開它' }],
  },
];

// 「帶我去複習」：open_page 帶 go，頁面直接切到 /english/review（抽屜與對話留著）。
const studioGo = [
  start,
  { type: 'tool_use', id: 't1', name: 'open_page', displayName: '開啟頁面' },
  {
    type: 'tool_result', id: 't1', name: 'open_page', ok: true, durationMs: 4, navigate: '/english/review',
    card: { kind: 'link', title: '複習', href: '/english/review', description: '間隔重複（SM-2）的單字卡複習', actionLabel: '前往' },
  },
  delta('已經帶你到**複習**頁了：看完正面想一想再翻牌，依記得的程度按「忘了／模糊／記得」，系統會排好下次複習的時間。'),
  done,
];

// 同一張圖，回答結束（沒有存 dashboard）：底下有「接著問」，圖表卡片可以自己換圖。
const dataChartDone = [
  ...dataChart.slice(0, 3),
  delta('北區營收連續五個月成長（820 → 1,060），南區則緩步下滑，中區持平。'),
  done,
];

// dashboard 頁的「AI 解讀」：read_dashboard 讀正在看的 dashboard（用最新資料重算），回答兩個重點。
const BOARD = '8f3c2a10-0000-4000-8000-000000000001';
const dashboardRead = [
  start,
  { type: 'tool_use', id: 't1', name: 'read_dashboard', displayName: '讀取 dashboard' },
  {
    type: 'tool_result', id: 't1', name: 'read_dashboard', ok: true, durationMs: 1300,
    card: { kind: 'link', title: '業績追蹤', href: `/dashboards/${BOARD}`, description: '2 張圖，剛用最新資料重算', actionLabel: '打開 dashboard' },
  },
  delta('「業績追蹤」最值得注意的兩件事：\n\n1. **北區是成長引擎**：每月營收連五個月上升（820 → 1,060，+29%），9 月已佔三區合計的 45%。\n2. **南區在下滑**：同期從 610 掉到 550（-10%），是三區裡唯一連續下降的，值得查是客戶流失還是單價問題。\n\n中區大致持平；「各地區營收佔比」的圓餅圖也反映同樣的結構——北區比重持續擴大。'),
  done,
];

const briefing = [
  start,
  { type: 'tool_use', id: 't1', name: 'google_calendar_events', displayName: 'Google 日曆' },
  { type: 'tool_use', id: 't2', name: 'learning_progress', displayName: '學習進度' },
  { type: 'tool_use', id: 't3', name: 'portfolio_summary', displayName: '投資組合' },
  {
    type: 'tool_result', id: 't1', name: 'google_calendar_events', ok: true, durationMs: 640,
    card: {
      kind: 'table', title: '今天（10/24）的行程', totalRows: null,
      columns: [{ key: 'time', label: '時間', numeric: false, nowrap: true }, { key: 'title', label: '行程', numeric: false }],
      rows: [
        { time: '09:30–10:00', title: '團隊站會' },
        { time: '14:00–15:30', title: '客戶需求訪談（台北辦公室）' },
        { time: '19:00–20:00', title: '健身' },
      ],
    },
  },
  {
    type: 'tool_result', id: 't2', name: 'learning_progress', ok: true, durationMs: 210,
    card: { kind: 'link', title: '12 個單字待複習', description: '連續 18 天・今日挑戰還沒做', href: '/english/review', actionLabel: '開始複習' },
  },
  { type: 'tool_result', id: 't3', name: 'portfolio_summary', ok: true, durationMs: 930 },
  delta('早安！這是你今天的簡報：\n\n**📅 今天的行程**：三個行程，10:00–14:00 整段空檔，下午的客戶訪談記得提早出發。\n\n**📚 學習**：12 個單字待複習，已連續 18 天——今日挑戰還沒做，5 題約 2 分鐘。\n\n**💰 資產**：淨值約 222 萬，未實現損益 +47 萬；長榮（2603）跌了 18%，是今天最值得留意的一檔。\n\n👉 建議先趁早上的空檔把 12 個單字複習完，保住連續紀錄。'),
  done,
];

// 「考我今天該複習的單字」：review_quiz 的 quiz 卡片，在卡片上直接作答。
const studioQuiz = [
  start,
  { type: 'tool_use', id: 't1', name: 'review_quiz', displayName: '複習小測驗' },
  {
    type: 'tool_result', id: 't1', name: 'review_quiz', ok: true, durationMs: 160,
    card: {
      kind: 'quiz', title: '複習小測驗',
      data: {
        fromDue: true, totalDue: 12,
        questions: [
          { userWordId: 11, word: 'serendipity', prompt: 'What is the meaning of "serendipity"?', options: ['the occurrence of events by chance in a happy way', 'a feeling of deep sadness', 'a formal agreement between two parties', 'the act of moving to another country'], correctIndex: 0, explanation: 'serendipity: the occurrence of events by chance in a happy way（意外發現美好事物的運氣）' },
          { userWordId: 12, word: 'ephemeral', prompt: 'Fashions are ___; they come and go every season.', options: ['durable', 'ephemeral', 'ancient', 'reluctant'], correctIndex: 1, explanation: 'ephemeral: lasting for a very short time（短暫的）' },
          { userWordId: 13, word: 'meticulous', prompt: '「一絲不苟的；非常仔細的」的英文是？', options: ['careless', 'generous', 'meticulous', 'obvious'], correctIndex: 2, explanation: 'meticulous: showing great attention to detail（一絲不苟的）' },
        ],
      },
    },
  },
  delta('出好了！三題都是你今天該複習的字，直接在上面的卡片作答，答完會記進複習排程。加油 💪'),
  done,
];

// 單字庫頁：「用我單字庫裡最近存的字寫一段短文」：saved_words 讀最近存的字，短文裡用粗體標出。
const studioWriting = [
  start,
  { type: 'tool_use', id: 't1', name: 'saved_words', displayName: '我的單字庫' },
  {
    type: 'tool_result', id: 't1', name: 'saved_words', ok: true, durationMs: 140,
    card: { kind: 'link', title: '我的單字庫', description: '共 86 個字，最近加入：serendipity、ephemeral、meticulous、resilient、candid', href: '/english/repository', actionLabel: '前往單字庫' },
  },
  delta('用你最近存的 5 個字寫了一段短文：\n\n> It was pure **serendipity** that I found the little bookshop. The owner, a **meticulous** old man, kept every book in perfect order. He was **candid** about his worries: the shop had survived two floods, and he called himself **resilient**, but he knew that, like all good things, it might be **ephemeral**.\n\n**中文翻譯**：我會發現那家小書店純屬**意外的幸運**。店主是位**一絲不苟**的老先生，每本書都排得整整齊齊。他**坦白**說出自己的擔憂：書店撐過兩次水災，他說自己很**有韌性**，但也知道，就像所有美好的事物一樣，它可能只是**短暫的**。'),
  done,
];

// 頁面內的 fetch 替身：/api/chatbot/chat 回一個照順序送出事件的串流。
// events 可以是一段串流，或是一串串流（第 n 次送出拿第 n 段，情境 F 的交棒用）。
const fakeChat = (scripts) => {
  const realFetch = window.fetch.bind(window);
  let calls = 0;
  window.fetch = (input, init) => {
    const url = typeof input === 'string' ? input : input.url;
    if (!url.includes('/api/chatbot/chat')) return realFetch(input, init);
    const events = Array.isArray(scripts[0]) ? scripts[Math.min(calls, scripts.length - 1)] : scripts;
    calls += 1;
    const enc = new TextEncoder();
    const body = new ReadableStream({
      async start(controller) {
        for (const e of events) {
          await new Promise((r) => setTimeout(r, 120));
          controller.enqueue(enc.encode(`data: ${JSON.stringify(e)}\n\n`));
        }
        if (events[events.length - 1].type === 'done') controller.close();
      },
    });
    return Promise.resolve(new Response(body, { headers: { 'Content-Type': 'text/event-stream' } }));
  };
};


// E：一句話做出整個 dashboard（第 19 輪）：三張圖一起查，再三個 save_to_dashboard 合在一張確認卡。
const chartCard = (id, title, viz, headers, rows) => ({
  type: 'tool_result', id, name: 'ask_data', ok: true, durationMs: 2100,
  card: { kind: 'chart', title, data: { viz, headers, groupHints: headers.slice(0, -1), temporalHints: headers[0] === '月份' ? ['月份'] : [], rows, plan: { viewBy: headers.slice(0, -1), measures: [{ field: headers[headers.length - 1], aggregation: 'sum' }] } } },
});
const months = ['2026-04', '2026-05', '2026-06', '2026-07', '2026-08', '2026-09'];
const wholeDashboard = [
  start,
  { type: 'tool_use', id: 't0', name: 'describe_data', displayName: '查看欄位' },
  { type: 'tool_result', id: 't0', name: 'describe_data', ok: true, durationMs: 300 },
  { type: 'tool_use', id: 't1', name: 'ask_data', displayName: '查詢資料' },
  { type: 'tool_use', id: 't2', name: 'ask_data', displayName: '查詢資料' },
  { type: 'tool_use', id: 't3', name: 'ask_data', displayName: '查詢資料' },
  chartCard('t1', '每月營收', 'line', ['月份', '營收'], months.map((m, i) => ({ 月份: m, 營收: 1800 + i * 120 + (i % 2) * 60 }))),
  chartCard('t2', '各地區營收占比', 'donut', ['地區', '營收'], [{ 地區: '北區', 營收: 4700 }, { 地區: '中區', 營收: 2850 }, { 地區: '南區', 營收: 2900 }]),
  chartCard('t3', '營收前 5 名產品', 'bar', ['產品', '營收'], [{ 產品: '筆電', 營收: 3200 }, { 產品: '螢幕', 營收: 2100 }, { 產品: '鍵盤', 營收: 1500 }, { 產品: '耳機', 營收: 1250 }, { 產品: '滑鼠', 營收: 900 }]),
  delta('我挑了三張互補的圖：**整體趨勢**（每月營收）、**組成**（各地區占比）和**排名**（前 5 名產品）。請確認要把它們存進新的「業績總覽」dashboard：'),
  {
    type: 'approval_required', pendingId: 'p1', tainted: false,
    calls: [
      { toolUseId: 's1', connector: 'site', connectorName: 'Jacky Studio', tool: 'save_to_dashboard', displayName: '存到 dashboard', effect: 'write', input: { chartId: 'chart-a1', dashboard: '業績總覽', title: '每月營收' }, summary: '把「每月營收」存到 dashboard「業績總覽」' },
      { toolUseId: 's2', connector: 'site', connectorName: 'Jacky Studio', tool: 'save_to_dashboard', displayName: '存到 dashboard', effect: 'write', input: { chartId: 'chart-b2', dashboard: '業績總覽', title: '各地區營收占比' }, summary: '把「各地區營收占比」存到 dashboard「業績總覽」' },
      { toolUseId: 's3', connector: 'site', connectorName: 'Jacky Studio', tool: 'save_to_dashboard', displayName: '存到 dashboard', effect: 'write', input: { chartId: 'chart-c3', dashboard: '業績總覽', title: '營收前 5 名產品', open: true }, summary: '把「營收前 5 名產品」存到 dashboard「業績總覽」，存好後打開它' },
    ],
  },
];

async function open(browser, viewport, events) {
  const page = await browser.newPage({ viewport });
  await page.addInitScript(fakeChat, events);
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    const p = url.pathname;
    const json = (body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
    if (p === '/api/account/info') return json({ data: user });
    if (p.startsWith('/api/chatbot/conversations')) return json({ data: [] });
    if (p === '/api/chatbot/attachments') return json({ data: { id: 7, fileName: 'trade-confirmation.png', kind: 'image', charCount: 0, truncated: false } });
    if (p === '/api/chatbot/attachments/7/content') return route.fulfill({ body: fs.readFileSync(path.join(__dirname, 'fixtures/trade-confirmation.png')), contentType: 'image/png' });
    if (p === '/api/dataanalysis/sources') return json({ data: [] });
    if (p === '/api/review/submit') return json({ data: { wordsReviewed: 3, correctCount: 2, xpEarned: 6 } });
    if (p.startsWith('/api/review/due')) return json({ data: { dueCount: 12 } });
    if (p.includes('dailychallenge/stats') || p.includes('DailyChallenge/stats')) return json({ data: { currentStreak: 18, todayCompleted: false, todayScore: null } });
    if (p === `/api/dashboard/${BOARD}`) return json({ data: {
      id: BOARD, name: '業績追蹤', visibility: 'Private', isOwner: true,
      widgets: [
        { id: 'w1', title: '各地區每月營收', vizType: 'line', x: 0, y: 0, w: 6, h: 8,
          plan: { viewBy: [{ type: 'DateTrunc', fieldName: 'OrderDate', alias: '月份', granularity: 'Month' }, { type: 'Field', fieldName: 'Region', alias: '地區' }], measures: [{ fieldName: 'Amount', aggregation: 'Sum', alias: '營收' }] } },
        { id: 'w2', title: '各地區營收佔比', vizType: 'pie', x: 6, y: 0, w: 6, h: 8,
          plan: { viewBy: [{ type: 'Field', fieldName: 'Region', alias: '地區' }], measures: [{ fieldName: 'Amount', aggregation: 'Sum', alias: '營收' }] } },
      ],
    } });
    if (p === `/api/dashboard/${BOARD}/widgets/w1/data`) return json({ data: { rows: ['2026-05', '2026-06', '2026-07', '2026-08', '2026-09'].flatMap((m, i) => [
      { 月份: m, 地區: '北區', 營收: 820 + i * 60 }, { 月份: m, 地區: '中區', 營收: 540 + i * 25 + (i % 2) * 40 }, { 月份: m, 地區: '南區', 營收: 610 - i * 15 },
    ]) } });
    if (p === `/api/dashboard/${BOARD}/widgets/w2/data`) return json({ data: { rows: [{ 地區: '北區', 營收: 4700 }, { 地區: '中區', 營收: 2850 }, { 地區: '南區', 營收: 2900 }] } });
    if (p === '/api/dataanalysis/schema') return json({ data: { fields: [
      { name: 'OrderDate', dataType: 'date', isNullable: false, canonicalType: 'DateTime' },
      { name: 'Region', dataType: 'nvarchar', isNullable: false, canonicalType: 'String' },
      { name: 'Amount', dataType: 'decimal', isNullable: false, canonicalType: 'Decimal' },
    ] } });
    if (p === '/api/dataanalysis/summarize/nl') return json({ data: {
      plan: { viewBy: [{ type: 'Field', fieldName: 'Region', alias: '地區' }], measures: [{ fieldName: 'Amount', aggregation: 'Sum', alias: '營收' }] },
      interpretation: '依地區加總 2026 年第三季的營收（Amount），由高到低排序',
      rows: [{ 地區: '北區', 營收: 3120 }, { 地區: '南區', 營收: 1790 }, { 地區: '中區', 營收: 1730 }],
      notes: ['資料裡沒有「東區」，已忽略這個條件'],
    } });
    if (p.startsWith('/api/')) return json({ data: null });
    const file = path.join(dist, p === '/' || !path.extname(p) ? 'index.html' : p);
    if (!fs.existsSync(file)) return route.fulfill({ status: 404, body: '' });
    return route.fulfill({ body: fs.readFileSync(file), contentType: types[path.extname(file)] || 'text/html' });
  });
  // 語音輸入的替身：按下麥克風後「聽到」一句話，停在聆聽中。
  await page.addInitScript(() => {
    window.SpeechRecognition = window.webkitSpeechRecognition = class {
      start() {
        setTimeout(() => this.onresult?.({ results: [Object.assign([{ transcript: '給我今天的簡報' }], { isFinal: false })] }), 300);
      }
      stop() { this.onend?.(); }
      abort() { this.onend?.(); }
    };
  });
  return page;
}

async function ask(page, text, placeholder) {
  const box = placeholder ? page.getByPlaceholder(placeholder).first() : page.getByRole('textbox').last();
  await box.click();
  await box.fill(text);
  await box.press('Enter');
  await page.waitForTimeout(2500);
}

const word = {
  id: 'w1', partitionKey: 'w1', word: 'resilient', kkPhonics: 'rɪˋzɪljənt', dateAdded: '2026-10-01', lastUpdated: '2026-10-01',
  seoTitle: 'resilient', seoDescription: '',
  meanings: [{ partOfSpeech: 'adjective', definitions: [{ english: 'able to recover quickly from difficult conditions', chinese: '有韌性的；能迅速恢復的' }],
    exampleSentences: [
      { english: 'Taiwan’s chip industry has proved remarkably resilient despite the global slowdown.', chinese: '儘管全球經濟放緩，台灣的晶片產業仍展現出驚人的韌性。' },
      { english: 'Children are often more resilient than adults expect.', chinese: '孩子往往比大人以為的更有韌性。' },
    ], synonyms: ['tough', 'hardy', 'adaptable'], antonyms: ['fragile'], relatedWords: ['resilience'] }],
};
const translate = [
  start,
  delta('**翻譯**：儘管全球經濟放緩，台灣的晶片產業仍展現出驚人的**韌性**。\n\n**值得學的字詞**\n\n| 字詞 | 意思 | 用法 |\n|---|---|---|\n| **prove** + 形容詞 | 證明是…、結果是… | *The plan proved successful.* |\n| **remarkably** | 非常地、驚人地 | 修飾形容詞，比 very 更有「出乎意料」的味道 |\n| **resilient** | 有韌性的 | 人、經濟、產業都能用 |\n| **despite** + 名詞 | 儘管 | 後面不加 of（✗ despite of） |\n| **slowdown** | 放緩、減速 | *an economic slowdown* |'),
  done,
];
async function selectIn(page, locator, text, last = false) {
  await locator.first().evaluate((el, [t, last]) => {
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    let n, hit;
    while ((n = walker.nextNode())) {
      const i = n.textContent.indexOf(t);
      if (i >= 0) { hit = [n, i]; if (!last) break; }
    }
    if (!hit) throw new Error('text not found: ' + t);
    const r = document.createRange();
    r.setStart(hit[0], hit[1]); r.setEnd(hit[0], hit[1] + t.length);
    const s = window.getSelection(); s.removeAllRanges(); s.addRange(r);
  }, [text, last]);
  await page.waitForTimeout(700);
}
// Studio AI 把整個 dashboard 交給 /data-analysis 的助理（open_page 的 go＋ask）。
const studioHandoff = [
  start,
  { type: 'tool_use', id: 't1', name: 'open_page', displayName: '開啟頁面' },
  {
    type: 'tool_result', id: 't1', name: 'open_page', ok: true, durationMs: 3, navigate: '/data-analysis',
    handoff: '幫我做一個業績總覽 dashboard（3 到 4 張圖），存完打開',
    card: { kind: 'link', title: '資料分析', href: '/data-analysis', description: '查詢與彙總表格資料，有自己的資料分析 AI 助理', actionLabel: '前往' },
  },
  delta('做 dashboard 要用資料分析的資料工具，我把這件事**交給資料分析的 AI 助理**了——它會先看欄位、畫三四張互補的圖，再請你確認存檔。'),
  done,
];
// 持股的市場面：Studio 先讀投資組合，再把代號交給 /finance 的助理（D-097）。
const holdingsHandoff = [
  start,
  { type: 'tool_use', id: 't1', name: 'portfolio_summary', displayName: '投資組合摘要' },
  {
    type: 'tool_result', id: 't1', name: 'portfolio_summary', ok: true, durationMs: 900,
    card: { kind: 'table', title: '我的持股', columns: [{ key: 'name', label: '名稱' }, { key: 'weight', label: '占比', numeric: true }],
      rows: [{ name: '2330 台積電', weight: '41%' }, { name: '0050 元大台灣50', weight: '27%' }, { name: '2454 聯發科', weight: '12%' }] },
  },
  { type: 'tool_use', id: 't2', name: 'open_page', displayName: '開啟頁面' },
  {
    type: 'tool_result', id: 't2', name: 'open_page', ok: true, durationMs: 3, navigate: '/finance',
    handoff: '我持有 2330 台積電、0050 元大台灣50、2454 聯發科，這幾檔最近外資在買還是賣？',
    card: { kind: 'link', title: 'Stock Intelligence 台股分析', href: '/finance', description: '會查即時台股資料的 AI 助理', actionLabel: '前往' },
  },
  delta('你的台股持股是台積電、元大台灣50 與聯發科。外資買賣超要查即時的市場資料，我把這三檔**交給 Stock Intelligence 的 AI 助理**了。'),
  done,
];
const financeFlows = [
  start,
  { type: 'tool_use', id: 't1', name: 'get_twse_institutional_investors_by_stock', displayName: '個股三大法人買賣超' },
  { type: 'tool_result', id: 't1', name: 'get_twse_institutional_investors_by_stock', ok: true, durationMs: 1500 },
  delta('**最近 5 個交易日外資動向**\n\n| 股票 | 外資買賣超（張） | 方向 |\n|---|---|---|\n| 2330 台積電 | −18,420 | 連 3 日賣超 |\n| 0050 元大台灣50 | +2,310 | 小幅買超 |\n| 2454 聯發科 | −3,050 | 賣超 |\n\n外資這週對**台積電與聯發科偏賣**，0050 則小幅回補。台積電佔你持股 41%，若外資續賣，短線波動會最直接反映在你的淨值上。'),
  done,
];
const only = (k) => !process.env.ONLY || process.env.ONLY === k;
(async () => {
  const out = process.argv[2];
  const browser = await chromium.launch();
  for (const [name, viewport] of [['desktop', { width: 1280, height: 900 }], ['phone', { width: 390, height: 844 }]]) {
    if (only('A')) {
    // A：字典頁選取例句 → 「解釋／翻成中文／問 AI」→ 按翻成中文 → Studio 打開、新對話、翻譯＋值得學的字
    let page = await open(browser, viewport, translate);
    await page.route('**/api/dictionary/resilient', (r) => r.fulfill({ contentType: 'application/json', body: JSON.stringify({ data: word }) }));
    await page.goto('http://localhost:5173/english/dictionary/resilient');
    await page.waitForTimeout(2500);
    const sentence = 'Taiwan’s chip industry has proved remarkably resilient despite the global slowdown.';
    await selectIn(page, page.locator('body'), sentence);
    await page.screenshot({ path: `${out}/select-sentence-${name}.png` });
    await page.getByRole('toolbar').getByText('翻成中文').click();
    await page.waitForTimeout(3000);
    await page.screenshot({ path: `${out}/select-translate-${name}.png` });
    await page.close();
    }
    if (only('B')) {
    // B：Studio 寫的短文裡選一個字 → 「查字典／加入單字庫／問 AI」
    page = await open(browser, viewport, studioWriting);
    await page.goto('http://localhost:5173/english/repository');
    await page.waitForTimeout(2000);
    await page.keyboard.press('Control+k');
    await page.waitForTimeout(800);
    await ask(page, '用我單字庫裡最近存的字寫一段短文');
    await page.waitForTimeout(1500);
    await selectIn(page, page.locator('[data-ai-assistant]'), 'ephemeral', true);
    await page.screenshot({ path: `${out}/select-word-studio-${name}.png` });
    await page.close();
    }
    if (only('C')) {
      // C：/finance 助理的回答裡選「多頭排列」→ 工具列（解釋／翻成英文／問 AI，沒有加入單字庫）→ 解釋，接在同一段對話
      const page = await open(browser, viewport, financeTrend);
      await page.goto('http://localhost:5173/finance');
      await page.waitForTimeout(2500);
      await page.locator('input[aria-label="AI 助手"]').click();
      await page.waitForTimeout(1200);
      const input = page.locator('[data-ai-assistant] textarea').first();
      await input.fill('台積電最近一個月的走勢怎麼樣？');
      await input.press('Enter');
      await page.waitForTimeout(2500);
      await page.waitForTimeout(1500);
      await selectIn(page, page.locator('[data-ai-assistant]'), '多頭排列', true);
      await page.screenshot({ path: `${out}/select-finance-${name}.png` });
      await page.getByRole('toolbar').getByText('解釋').click();
      await page.waitForTimeout(3500);
      await page.screenshot({ path: `${out}/select-finance-explain-${name}.png` });
      await page.close();
    }
    if (only('D')) {
      // D：/data-analysis 助理的回答裡選「南區則緩步下滑」→ 工具列（名稱是這一頁的助理：解釋／翻成英文／問 AI）
      const page = await open(browser, viewport, dataChartDone);
      await page.goto('http://localhost:5173/data-analysis');
      await page.waitForTimeout(2000);
      await page.getByPlaceholder(/問 AI 助手/).first().click();
      await page.waitForTimeout(800);
      await ask(page, '畫出各地區每月營收', /問你的資料/);
      await page.waitForTimeout(1000);
      await selectIn(page, page.locator('[data-ai-assistant]'), '南區則緩步下滑', true);
      await page.screenshot({ path: `${out}/select-data-${name}.png` });
      // 按下去之後的畫面不拍：假的串流每次回同一段，第二個回答看起來跟第一個一樣（已用 console 確認送出的是
      // 「請解釋…「南區則緩步下滑」」、conversationId c1、沒有 quotesPageText，訊息框也捲到最下面）。
      await page.close();
    }
    if (only('E')) {
      // E：/data-analysis 歡迎畫面的第四個建議「幫我做一個業績總覽 dashboard」→ 三張圖 → 一張確認卡存三張
      const page = await open(browser, viewport, wholeDashboard);
      await page.goto('http://localhost:5173/data-analysis');
      await page.waitForTimeout(2000);
      await page.getByPlaceholder(/問 AI 助手/).first().click();
      await page.waitForTimeout(800);
      await page.screenshot({ path: `${out}/data-whole-dashboard-welcome-${name}.png` });
      await ask(page, '幫我做一個業績總覽 dashboard（3 到 4 張圖），存完打開', /問你的資料/);
      await page.waitForTimeout(1500);
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: `${out}/data-whole-dashboard-${name}.png` });
      await page.close();
    }
    if (only('F')) {
      // F：Studio AI 交棒（D-095）——首頁 ⌘K「幫我做一個業績總覽 dashboard」→ Studio 用 open_page（go＋ask）交給
      // /data-analysis 的助理 → 回答結束後頁面切過去，那裡的助理自己展開並送出同一個請求 → 三張圖＋一張確認卡。
      const page = await open(browser, viewport, [studioHandoff, wholeDashboard]);
      await page.goto('http://localhost:5173/');
      await page.waitForTimeout(1500);
      await page.keyboard.press('Control+k');
      await page.waitForTimeout(800);
      await page.getByRole('textbox').last().fill('幫我做一個業績總覽 dashboard，存完打開');
      await page.getByRole('textbox').last().press('Enter');
      await page.waitForTimeout(700);
      await page.screenshot({ path: `${out}/studio-handoff-${name}.png` });
      await page.waitForTimeout(6000);
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: `${out}/studio-handoff-data-${name}.png` });
      // 第 21 輪（D-099）：捲到對話開頭，看「由 Studio AI 轉來」
      await page.locator('[data-testid="user-message"]').first().evaluate((el) => el.scrollIntoView({ block: 'start' }));
      await page.waitForTimeout(400);
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: `${out}/studio-handoff-from-${name}.png` });
      console.log('F', name, page.url());
      await page.close();
    }
    if (only('G')) {
      // G：/portfolio 的 Studio 建議「我的持股最近外資在買還是賣？」→ 讀持股 → 交給 /finance 的助理 → 那裡回答
      const page = await open(browser, viewport, [holdingsHandoff, financeFlows]);
      await page.goto('http://localhost:5173/portfolio');
      await page.waitForTimeout(1500);
      await page.keyboard.press('Control+k');
      await page.waitForTimeout(800);
      await page.screenshot({ path: `${out}/studio-holdings-welcome-${name}.png` });
      await page.getByText('我的持股最近外資在買還是賣？').first().click();
      await page.getByRole('textbox').last().press('Enter'); // 建議只放進輸入框
      await page.waitForTimeout(750);
      await page.screenshot({ path: `${out}/studio-holdings-handoff-${name}.png` });
      await page.waitForTimeout(6000);
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: `${out}/studio-holdings-finance-${name}.png` });
      console.log('G', name, page.url());
      await page.close();
    }
    if (only('H')) {
      // H：/data-analysis 下方的「問 AI」：回答（解讀、提醒、結果表）裡選字 → 頁面助理的工具列（選取巡檢）
      const page = await open(browser, viewport, dataChartDone);
      await page.goto('http://localhost:5173/data-analysis');
      await page.waitForTimeout(2000);
      await page.getByRole('button', { name: 'ask AI' }).click();
      await page.waitForTimeout(500);
      const q = page.getByPlaceholder(/EMEA/);
      await q.fill('第三季各地區營收，東區也要');
      await q.press('Control+Enter');
      await page.waitForTimeout(2500);
      await page.screenshot({ path: `${out}/select-nl-result-${name}.png`, fullPage: true });
      await page.getByText('依地區加總 2026 年第三季的營收').scrollIntoViewIfNeeded();
      await page.evaluate(() => window.scrollBy(0, 200));
      await selectIn(page, page.locator('body'), '依地區加總 2026 年第三季的營收', false);
      await page.screenshot({ path: `${out}/select-nl-interpretation-${name}.png` });
      console.log('H toolbar', await page.getByRole('toolbar').count(), await page.getByRole('toolbar').allInnerTexts());
      await page.close();
    }
  }
  await browser.close();
})();
