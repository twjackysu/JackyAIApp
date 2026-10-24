// /connectors 的截圖（迴圈專用，合併前與 .loop/ 一起刪）：dist/ + 假 API。
// 用法：cd jackyaiapp.client && npm run build && NODE_PATH=$(npm root -g) node ../.loop/shots/connectors.cjs <輸出目錄>
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

(async () => {
  const browser = await chromium.launch();
  for (const [name, viewport] of [['desktop', { width: 1280, height: 900 }], ['phone', { width: 390, height: 844 }]]) {
    const page = await browser.newPage({ viewport });
    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      const p = url.pathname;
      const json = (body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
      if (p === '/api/account/info') return json({ data: user });
      if (p === '/api/connectors/status') return json([]);
      if (p === '/api/connectors/catalog') return json({ data: process.env.EMPTY ? { entries: [], installed: [], customLimit: 10 } : catalog });
      if (p === '/api/connectors/site-tokens') return json({ data: tokens });
      if (p === '/api/connectors/installations/probe') return json({ data: { serverUrl: 'https://kb.example.com/mcp', auth: 'apiKey', authorizationServer: null, registration: null, serverName: '公司內部知識庫' } });
      if (p === '/api/connectors/installations' && route.request().method() === 'POST') return json({ data: { ...catalog.installed[3], id: 'i-9' } });
      if (p.startsWith('/api/')) return json({ data: null });
      const file = path.join(dist, p === '/' || !path.extname(p) ? 'index.html' : p);
      if (!fs.existsSync(file)) return route.fulfill({ status: 404, body: '' });
      return route.fulfill({ body: fs.readFileSync(file), contentType: types[path.extname(file)] || 'text/html' });
    });
    await page.goto('http://localhost:5173/connectors');
    await page.waitForTimeout(1500);
    const section = page.getByText('讓其他 AI 使用本站').first();
    await section.scrollIntoViewIfNeeded();
    await page.waitForTimeout(300);
    await page.screenshot({ path: `${out}/site-mcp-section-${name}.png` });
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(300);
    await page.screenshot({ path: `${out}/connectors-top-${name}.png`, fullPage: true });
    await page.getByText('DeepWiki').first().click();
    await page.waitForTimeout(600);
    await page.screenshot({ path: `${out}/connectors-drawer-deepwiki-${name}.png` });
    await page.keyboard.press('Escape');
    await page.waitForTimeout(400);
    await page.getByText('公司內部知識庫').first().click();
    await page.waitForTimeout(600);
    await page.screenshot({ path: `${out}/connectors-drawer-custom-${name}.png` });
    await page.keyboard.press('Escape');
    await page.waitForTimeout(400);
    await page.getByText('Jacky Studio 本站功能').first().click();
    await page.waitForTimeout(600);
    await page.screenshot({ path: `${out}/connectors-drawer-site-${name}.png` });
    await page.keyboard.press('Escape');
    await page.waitForTimeout(400);

    // 新增 MCP server 對話框的三個步驟：網址 → 認證（伺服器要 API key）→ 完成（列出工具）
    await page.getByRole('button', { name: '新增 MCP server' }).click();
    await page.waitForTimeout(500);
    await page.getByLabel('MCP server 網址').fill('https://kb.example.com/mcp');
    await page.screenshot({ path: `${out}/connectors-add-1-${name}.png` });
    await page.getByRole('button', { name: '檢查' }).click();
    await page.waitForTimeout(600);
    await page.getByLabel('Token').fill('sk-live-0123456789');
    await page.waitForTimeout(400); // 「連線」從停用變可用的轉場
    await page.screenshot({ path: `${out}/connectors-add-2-${name}.png` });
    await page.getByRole('button', { name: '連線' }).click();
    await page.waitForTimeout(800);
    await page.screenshot({ path: `${out}/connectors-add-3-${name}.png` });
    await page.close();
  }
  await browser.close();
})();
