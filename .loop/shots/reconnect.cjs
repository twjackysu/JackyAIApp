// 第 62 輪：connector 需要重新連線時，失敗的呼叫附上「前往重新連線」卡片（D-223）。
// Studio AI 問 Notion → Notion 的工具因認證被拒失敗（✗ 的 chip 下方是連結卡）→ 按「前往重新連線」→ /connectors 打開 Notion 的詳情抽屜（重新授權）。
// 迴圈專用，合併前與 .loop/ 一起刪。用法：cd jackyaiapp.client && npm run build && NODE_PATH=$(npm root -g) node ../.loop/shots/reconnect.cjs <輸出目錄>
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const dist = path.resolve(__dirname, '../../jackyaiapp.client/dist');
const out = process.argv[2] || '/tmp/shots';
fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.json': 'application/json', '.woff2': 'font/woff2' };
const user = { id: 'u1', name: 'Jacky', email: 'jacky@example.com', isAdmin: false, drawdownBrakeEnabled: false };

const tool = (name, title, description, effect, permission) => ({ name, title, description, effect, permission });
const catalog = {
  customLimit: 10,
  entries: [
    { key: 'tw-stock', displayName: '台股資料', description: '證交所、櫃買、期交所的公開資料', category: 'finance', publisher: 'Jacky Studio', authKind: 'none', docsUrl: null, installed: false },
    { key: 'deepwiki', displayName: 'DeepWiki', description: '問任何 GitHub repo 的問題', category: 'dev', publisher: 'Cognition', authKind: 'none', docsUrl: 'https://docs.devin.ai/work-with-devin/deepwiki-mcp', installed: true },
    { key: 'notion', displayName: 'Notion', description: '搜尋與編輯你的 Notion 頁面', category: 'productivity', publisher: 'Notion', authKind: 'oauth', docsUrl: 'https://developers.notion.com/docs/mcp', installed: true },
    { key: 'linear', displayName: 'Linear', description: '查詢與建立 Linear 的 issue', category: 'productivity', publisher: 'Linear', authKind: 'oauth', docsUrl: 'https://linear.app/docs/mcp', installed: false },
  ],
  installed: [
    { id: null, definitionKey: 'site', displayName: 'Jacky Studio 本站功能', description: '字典、單字庫、學習進度、投資組合、dashboard', category: 'site', serverUrl: null, authKind: 'none', health: 'ok', alwaysOn: true, lastError: null, lastTestedAt: null, scopes: [],
      tools: [
        tool('look_up_word', '查字典', '查一個英文單字的意思與例句', 'read', 'auto'),
        tool('add_word_to_repository', '加入單字庫', '把單字加入使用者的單字庫', 'write', 'confirm'),
        tool('learning_progress', '學習進度', '待複習的單字、每日挑戰、連續天數', 'read', 'auto'),
        tool('portfolio_summary', '投資組合', '使用者的持倉與報酬', 'read', 'auto'),
        tool('read_dashboard', '讀取 dashboard', '讀使用者自己的 dashboard', 'read', 'auto'),
        tool('google_calendar_events', 'Google 日曆', '讀今天、明天或 7 天內的行程', 'read', 'auto'),
      ] },
    { id: 'i-1', definitionKey: 'deepwiki', displayName: 'DeepWiki', description: '問任何 GitHub repo 的問題', category: 'dev', serverUrl: 'https://mcp.deepwiki.com/mcp', authKind: 'none', health: 'ok', alwaysOn: false, lastError: null, lastTestedAt: '2026-10-24T11:00:00Z', scopes: [],
      tools: [
        tool('read_wiki_structure', '讀 wiki 目錄', '列出 repo 文件的主題', 'read', 'auto'),
        tool('read_wiki_contents', '讀 wiki', '讀 repo 的文件', 'read', 'auto'),
        tool('ask_question', '問問題', '問一個 repo 的問題', 'read', 'auto'),
      ] },
    { id: 'i-2', definitionKey: 'notion', displayName: 'Notion', description: '搜尋與編輯你的 Notion 頁面', category: 'productivity', serverUrl: 'https://mcp.notion.com/mcp', authKind: 'oauth', health: 'needsAuth', alwaysOn: false, lastError: 'Notion 不再接受這個登入，請重新授權', lastTestedAt: '2026-10-24T10:00:00Z', scopes: [],
      tools: [
        tool('notion-search', 'Search', 'Search Notion pages', 'read', 'auto'),
        tool('notion-create-pages', 'Create pages', 'Create Notion pages', 'write', 'confirm'),
        tool('notion-update-page', 'Update page', 'Update a Notion page', 'write', 'confirm'),
      ] },
    { id: 'i-3', definitionKey: 'custom', displayName: '公司內部知識庫', description: 'https://kb.example.com/mcp', category: 'data', serverUrl: 'https://kb.example.com/mcp', authKind: 'bearer', health: 'ok', alwaysOn: false, lastError: null, lastTestedAt: '2026-10-24T09:30:00Z', scopes: [],
      tools: [
        tool('search', null, 'Search the knowledge base', 'read', 'auto'),
        tool('delete_article', null, 'Delete an article', 'destructive', 'confirm'),
      ] },
  ],
};

const events = [
  { type: 'start', conversationId: 'c1', userMessageId: 1 },
  { type: 'tool_use', id: 't1', name: 'm2__notion-search', displayName: 'Notion・Search' },
  {
    type: 'tool_result', id: 't1', name: 'm2__notion-search', ok: false, durationMs: 640,
    card: { kind: 'link', title: '重新連線「Notion」', href: '/connectors?connector=i-2', description: '它不接受目前的認證，更新後再問一次', actionLabel: '前往重新連線' },
  },
  { type: 'delta', text: 'Notion 目前不接受你的登入（授權可能過期了），所以我沒辦法搜尋你的頁面。\n\n按上面的「**前往重新連線**」重新授權 Notion，回來再問我一次就好。' },
  { type: 'done', conversationId: 'c1', assistantMessageId: 2 },
];

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
        controller.close();
      },
    });
    return Promise.resolve(new Response(body, { headers: { 'Content-Type': 'text/event-stream' } }));
  };
};

(async () => {
  const browser = await chromium.launch();
  for (const [name, viewport] of [['desktop', { width: 1280, height: 900 }], ['phone', { width: 390, height: 844 }]]) {
    const page = await browser.newPage({ viewport });
    page.on('pageerror', (e) => console.log(name, 'PAGEERROR', e.message));
    await page.addInitScript(fakeChat, events);
    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      const p = url.pathname;
      const json = (body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
      if (p === '/api/account/info') return json({ data: user });
      if (p === '/api/connectors/status') return json([]);
      if (p === '/api/connectors/catalog') return json({ data: catalog });
      if (p.startsWith('/api/chatbot/conversations')) return json({ data: [] });
      if (p.startsWith('/api/')) return json({ data: null });
      const file = path.join(dist, p === '/' || !path.extname(p) ? 'index.html' : p);
      if (!fs.existsSync(file)) return route.fulfill({ status: 404, body: '' });
      return route.fulfill({ body: fs.readFileSync(file), contentType: types[path.extname(file)] || 'text/html' });
    });
    await page.goto('http://localhost:5173/');
    await page.waitForTimeout(1500);
    await page.keyboard.press('Control+k');
    await page.waitForTimeout(1000);
    const box = page.getByRole('textbox').last();
    await box.click();
    await box.fill('幫我在 Notion 找上週的會議紀錄');
    await box.press('Enter');
    await page.waitForTimeout(2500);
    await page.screenshot({ path: `${out}/studio-reconnect-${name}.png` });
    await page.getByRole('link', { name: /前往重新連線/ }).last().click();
    await page.waitForTimeout(1500);
    console.log(name, 'url =', new URL(page.url()).pathname + new URL(page.url()).search,
      '| drawer 重新授權 =', await page.getByRole('button', { name: '重新授權' }).isVisible().catch(() => false));
    await page.screenshot({ path: `${out}/connectors-reconnect-${name}.png` });
    await page.close();
  }
  await browser.close();
})();
