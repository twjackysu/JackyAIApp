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
      if (p === '/api/connectors/catalog') return json({ data: { entries: [], installed: [], customLimit: 10 } });
      if (p === '/api/connectors/site-tokens') return json({ data: tokens });
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
    await page.close();
  }
  await browser.close();
})();
