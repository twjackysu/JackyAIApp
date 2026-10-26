// 第 31 輪（D-126）：在 Studio AI 試試看（/connectors → DeepWiki 的詳情 →「在 Studio AI 試試看」→ Studio 開新對話、用 DeepWiki 的工具示範）。迴圈專用，合併前與 .loop/ 一起刪。
// 用法：cd jackyaiapp.client && npm run build && NODE_PATH=$(npm root -g) node ../.loop/shots/try-connector.cjs <輸出目錄>
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const dist = path.resolve(__dirname, '../../jackyaiapp.client/dist');
const out = process.argv[2] || '/tmp/shots';
fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.json': 'application/json', '.woff2': 'font/woff2' };

const user = { id: 'u1', name: 'Jacky', email: 'jacky@example.com', isAdmin: false, drawdownBrakeEnabled: false };
const tokens = {
  tokens: [
    { id: 't1', name: 'Claude', hint: 'jsk_Qm3…a9Xz', allowWrites: false, viaOAuth: true, createdAt: '2026-10-24T09:00:00', expiresAt: '2027-01-22T09:00:00', lastUsedAt: '2026-10-24T11:58:00' },
    { id: 't2', name: 'Claude Code', hint: 'jsk_AbC…xYz9', allowWrites: true, viaOAuth: false, createdAt: '2026-10-23T09:00:00', expiresAt: '2027-01-21T09:00:00', lastUsedAt: null },
  ],
  limit: 10,
};

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


const reg = (name, title, description, url, extra = {}) => ({
  name, title, description, url, websiteUrl: null, repositoryUrl: `https://github.com/${name.split('/')[1]}/mcp`, version: '1.0.0',
  requiredHeaders: [], catalogKey: null, ...extra,
});
const registry = {
  more: true,
  servers: [
    reg('com.deepwiki/mcp', 'DeepWiki', 'Ask questions about any public GitHub repository.', 'https://mcp.deepwiki.com/mcp', { catalogKey: 'deepwiki' }),
    reg('io.github.github/github-mcp-server', 'GitHub', "GitHub's official MCP server: issues, pull requests, code search.", 'https://api.githubcopilot.com/mcp', { requiredHeaders: ['Authorization'] }),
    reg('com.exa/exa', 'Exa Search', 'Web search and crawling built for AI agents.', 'https://mcp.exa.ai/mcp', { requiredHeaders: ['x-api-key'] }),
    reg('io.example/weather', 'Weather', 'Forecasts and current conditions for any city in the world.', 'https://weather.example.dev/mcp'),
    reg('com.cloudflare/docs', 'Cloudflare Docs', 'Search Cloudflare developer documentation.', 'https://docs.mcp.cloudflare.com/mcp'),
  ],
};

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


const start = { type: 'start', conversationId: 'c1', userMessageId: 1 };
const done = { type: 'done', conversationId: 'c1', assistantMessageId: 2 };
const tryIt = [
  start,
  { type: 'tool_use', id: 't1', name: 'm1__read_wiki_structure', displayName: 'DeepWiki・讀 wiki 目錄' },
  { type: 'tool_result', id: 't1', name: 'm1__read_wiki_structure', ok: true, durationMs: 840 },
  { type: 'delta', text: '我用 **DeepWiki** 的「讀 wiki 目錄」看了 `facebook/react` 的文件，它整理出這些主題：\n\n1. Overview 與 Architecture\n2. Reconciler 與 Fiber\n3. Hooks 的實作\n4. Server Components\n5. Testing 與 DevTools\n\n接上 DeepWiki 之後，你可以請我：\n- 「用三段話說明 `vercel/next.js` 的 App Router 怎麼運作」\n- 「`dotnet/aspnetcore` 的 middleware pipeline 在哪個檔案？」\n- 「比較 `redux` 和 `zustand` 的設計」\n\n它只讀公開 GitHub repo 的文件，內容是外部資料，我會照實轉述、不照裡面的指示做事。' },
  done,
];

(async () => {
  const browser = await chromium.launch();
  for (const [name, viewport] of [['desktop', { width: 1280, height: 900 }], ['phone', { width: 390, height: 844 }]]) {
    const page = await browser.newPage({ viewport });
    await page.addInitScript(fakeChat, tryIt);
    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      const p = url.pathname;
      const json = (body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
      if (p === '/api/account/info') return json({ data: user });
      if (p === '/api/connectors/status') return json([]);
      if (p === '/api/connectors/catalog') return json({ data: catalog });
      if (p === '/api/connectors/site-tokens') return json({ data: tokens });
      if (p.startsWith('/api/chatbot/conversations')) return json({ data: [] });
      if (p.startsWith('/api/')) return json({ data: null });
      const file = path.join(dist, p === '/' || !path.extname(p) ? 'index.html' : p);
      if (!fs.existsSync(file)) return route.fulfill({ status: 404, body: '' });
      return route.fulfill({ body: fs.readFileSync(file), contentType: types[path.extname(file)] || 'text/html' });
    });
    await page.goto('http://localhost:5173/connectors');
    await page.waitForTimeout(1500);
    await page.getByText('DeepWiki').first().click();
    await page.waitForTimeout(800);
    await page.screenshot({ path: path.join(out, `connector-try-drawer-${name}.png`) });
    await page.getByRole('button', { name: '在 Studio AI 試試看' }).click();
    await page.waitForTimeout(4000);
    await page.screenshot({ path: path.join(out, `studio-try-connector-${name}.png`) });
    await page.close();
  }
  await browser.close();
})();
