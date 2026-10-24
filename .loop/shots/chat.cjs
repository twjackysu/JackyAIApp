// Studio AI 與 /data-analysis 助理的截圖（迴圈專用，合併前與 .loop/ 一起刪）：dist/ + 假 API + 假的 chat 串流。
// 用法：cd jackyaiapp.client && npm run build && NODE_PATH=$(npm root -g) node ../.loop/shots/chat.cjs <輸出目錄>
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
  delta('北區營收連續五個月成長（820 → 1,060），南區則緩步下滑。我把這張圖存到「業績追蹤」dashboard，請確認：'),
  {
    type: 'approval_required', pendingId: 'p1', tainted: false,
    calls: [{ toolUseId: 't2', connector: 'site', connectorName: 'Jacky Studio', tool: 'save_to_dashboard', displayName: '存到 dashboard', effect: 'write', input: { chartId: 'k3f9', dashboard: '業績追蹤' }, summary: '把「各地區每月營收」存到 dashboard「業績追蹤」（新建）' }],
  },
];

// 同一張圖，回答結束（沒有存 dashboard）：底下有「接著問」，圖表卡片可以自己換圖。
const dataChartDone = [
  ...dataChart.slice(0, 3),
  delta('北區營收連續五個月成長（820 → 1,060），南區則緩步下滑，中區持平。'),
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

// 頁面內的 fetch 替身：/api/chatbot/chat 回一個照順序送出事件的串流。
const fakeChat = (events) => {
  const realFetch = window.fetch.bind(window);
  window.fetch = (input, init) => {
    const url = typeof input === 'string' ? input : input.url;
    if (!url.includes('/api/chatbot/chat')) return realFetch(input, init);
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

async function open(browser, viewport, events) {
  const page = await browser.newPage({ viewport });
  await page.addInitScript(fakeChat, events);
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    const p = url.pathname;
    const json = (body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
    if (p === '/api/account/info') return json({ data: user });
    if (p.startsWith('/api/chatbot/conversations')) return json({ data: [] });
    if (p === '/api/dataanalysis/sources') return json({ data: [] });
    if (p.startsWith('/api/review/due')) return json({ data: { dueCount: 12 } });
    if (p.includes('dailychallenge/stats') || p.includes('DailyChallenge/stats')) return json({ data: { currentStreak: 18, todayCompleted: false, todayScore: null } });
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

(async () => {
  const browser = await chromium.launch();
  for (const [name, viewport] of [['desktop', { width: 1280, height: 900 }], ['phone', { width: 390, height: 844 }]]) {
    // 首頁 →「AI 今日簡報」→ Studio 一次叫三個工具
    let briefingPage = await open(browser, viewport, briefing);
    await briefingPage.goto('http://localhost:5173/');
    await briefingPage.waitForTimeout(1500);
    await briefingPage.screenshot({ path: `${out}/home-briefing-card-${name}.png` });
    await briefingPage.getByText('AI 今日簡報').click();
    await briefingPage.waitForTimeout(3000);
    await briefingPage.screenshot({ path: `${out}/studio-briefing-${name}.png` });
    await briefingPage.close();

    // Studio：首頁 → ⌘K → 查字並加入單字庫（停在確認卡）
    let page = await open(browser, viewport, studioWord);
    await page.goto('http://localhost:5173/');
    await page.waitForTimeout(1500);
    await page.keyboard.press('Control+k');
    await page.waitForTimeout(800);
    await ask(page, 'serendipity 是什麼？幫我加入單字庫');
    await page.screenshot({ path: `${out}/studio-approval-${name}.png` });
    await page.close();

    // Studio：語音輸入（聆聽中）
    page = await open(browser, viewport, studioWord);
    await page.goto('http://localhost:5173/english');
    await page.waitForTimeout(1500);
    await page.keyboard.press('Control+k');
    await page.waitForTimeout(800);
    await page.getByRole('button', { name: '語音輸入' }).click();
    await page.waitForTimeout(800);
    await page.screenshot({ path: `${out}/studio-voice-${name}.png` });
    await page.close();

    // Studio：/portfolio → ⌘K → 持倉表格
    page = await open(browser, viewport, studioPortfolio);
    await page.goto('http://localhost:5173/portfolio');
    await page.waitForTimeout(1500);
    await page.keyboard.press('Control+k');
    await page.waitForTimeout(800);
    await ask(page, '幫我看看我的持股，哪幾檔虧最多？');
    await page.screenshot({ path: `${out}/studio-portfolio-${name}.png` });
    await page.close();

    // /data-analysis：最上方的對話 → 圖表卡片 → 存到 dashboard 的確認卡
    page = await open(browser, viewport, dataChart);
    await page.goto('http://localhost:5173/data-analysis');
    await page.waitForTimeout(2000);
    await page.getByPlaceholder(/問 AI 助手/).first().click(); // 一行的對話框：聚焦後展開成完整對話
    await page.waitForTimeout(800);
    await ask(page, '畫出各地區每月營收，存到「業績追蹤」dashboard', /問你的資料/);
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: `${out}/data-analysis-save-${name}.png` });
    await page.close();

    // /data-analysis：回答結束 → 圖表卡片換成直條圖、底下的「接著問」
    page = await open(browser, viewport, dataChartDone);
    await page.goto('http://localhost:5173/data-analysis');
    await page.waitForTimeout(2000);
    await page.getByPlaceholder(/問 AI 助手/).first().click();
    await page.waitForTimeout(800);
    await ask(page, '畫出各地區每月營收', /問你的資料/);
    await page.getByRole('button', { name: /直條圖/ }).first().click();
    await page.waitForTimeout(600);
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: `${out}/data-analysis-followups-${name}.png` });
    await page.close();
  }
  await browser.close();
})();
