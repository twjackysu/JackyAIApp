// 第 30 輪（D-124）：Studio AI 的 find_mcp_server（/connectors 的建議 → mcp-servers 卡片 → 按「加入」→ /connectors 的新增對話框已填好搜尋）。迴圈專用，合併前與 .loop/ 一起刪。
// 用法：cd jackyaiapp.client && npm run build && NODE_PATH=$(npm root -g) node ../.loop/shots/find-mcp.cjs <輸出目錄>
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
const find = [
  start,
  { type: 'tool_use', id: 't1', name: 'find_mcp_server', displayName: '找 MCP server' },
  {
    type: 'tool_result', id: 't1', name: 'find_mcp_server', ok: true, durationMs: 220,
    card: {
      kind: 'mcp-servers', title: 'MCP 官方目錄：weather',
      data: {
        search: 'weather', more: false,
        servers: [
          { name: 'io.example/weather', title: 'Weather', description: 'Forecasts and current conditions for any city in the world.', host: 'weather.example.dev', needs: '', inCatalog: false },
          { name: 'com.openmeteo/forecast', title: 'Open-Meteo Forecast', description: 'Free weather forecast API: hourly and daily, 16 days ahead.', host: 'mcp.open-meteo.example', needs: '需要 API key（x-api-key）', inCatalog: false },
        ],
      },
    },
  },
  { type: 'delta', text: '在 MCP 官方目錄找到兩個查天氣的 remote MCP server：\n\n- **Weather**（weather.example.dev）：不用登入，查任何城市的即時天氣與預報，最適合直接用。\n- **Open-Meteo Forecast**：可查 16 天內的逐時預報，但要準備一把 API key。\n\n按卡片上的「加入」會到 Connectors，由你自己確認它的工具與權限後連線（只接你信任的 server）。接上之後，問我「明天台北會下雨嗎？」我就能用它回答。' },
  done,
];

(async () => {
  const browser = await chromium.launch();
  for (const [name, viewport] of [['desktop', { width: 1280, height: 900 }], ['phone', { width: 390, height: 844 }]]) {
    const page = await browser.newPage({ viewport });
    await page.addInitScript(fakeChat, find);
    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      const p = url.pathname;
      const json = (body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
      if (p === '/api/account/info') return json({ data: user });
      if (p === '/api/connectors/status') return json([]);
      if (p === '/api/connectors/catalog') return json({ data: catalog });
      if (p === '/api/connectors/site-tokens') return json({ data: tokens });
      if (p.startsWith('/api/chatbot/conversations')) return json({ data: [] });
      if (p === '/api/connectors/registry') {
        const q = (url.searchParams.get('search') || '').toLowerCase();
        const servers = registry.servers.filter((s) => s.name.includes(q));
        return json({ data: { servers, more: !q && registry.more } });
      }
      if (p.startsWith('/api/')) return json({ data: null });
      const file = path.join(dist, p === '/' || !path.extname(p) ? 'index.html' : p);
      if (!fs.existsSync(file)) return route.fulfill({ status: 404, body: '' });
      return route.fulfill({ body: fs.readFileSync(file), contentType: types[path.extname(file)] || 'text/html' });
    });
    await page.goto('http://localhost:5173/connectors');
    await page.waitForTimeout(1500);
    await page.keyboard.press('Control+k');
    await page.waitForTimeout(1000);
    await page.getByText('幫我找一個能查天氣的 MCP server').first().click();
    await page.waitForTimeout(300);
    if (await page.getByRole('textbox').last().inputValue()) await page.getByRole('textbox').last().press('Enter');
    await page.waitForTimeout(3000);
    await page.screenshot({ path: path.join(out, `studio-find-mcp-${name}.png`) });
    await page.getByRole('link', { name: '到 Connectors 加入 Weather' }).click();
    await page.waitForTimeout(1500);
    // The drawer steps aside by itself on /connectors?registry= (D-125), so the dialog is in front on a phone too.
    console.log(name, page.url());
    await page.screenshot({ path: path.join(out, `studio-find-mcp-added-${name}.png`) });
    await page.close();
  }
  await browser.close();
})();
