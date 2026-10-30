// 未登入的 Studio AI（迴圈專用，合併前與 .loop/ 一起刪）：右下角按鈕 → 「登入後可使用 Studio AI」（第 61 輪，D-217）。
// 用法：cd jackyaiapp.client && npm run build && NODE_PATH=$(npm root -g) node ../.loop/shots/guest.cjs <輸出目錄>
// 輸出 studio-guest-<desktop|phone>.png，並印出登入按鈕的網址。
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const dist = path.resolve(__dirname, '../../jackyaiapp.client/dist');
const out = process.argv[2] || '/tmp/shots';
fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.json': 'application/json', '.woff2': 'font/woff2' };

(async () => {
  const browser = await chromium.launch();
  for (const [name, viewport] of [['desktop', { width: 1280, height: 900 }], ['phone', { width: 390, height: 844 }]]) {
    const page = await browser.newPage({ viewport });
    await page.route('**/*', async (route) => {
      const p = new URL(route.request().url()).pathname;
      const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
      if (p === '/api/account/info') return json({ error: 'Not authenticated' }, 401);
      if (p.startsWith('/api/')) return json({ data: null });
      const file = path.join(dist, p === '/' || !path.extname(p) ? 'index.html' : p);
      if (!fs.existsSync(file)) return route.fulfill({ status: 404, body: '' });
      return route.fulfill({ body: fs.readFileSync(file), contentType: types[path.extname(file)] || 'text/html' });
    });
    await page.goto('http://localhost/english/dictionary?word=serendipity');
    await page.waitForSelector('button[aria-label="開啟 Studio AI"]');
    await page.waitForTimeout(400);
    await page.screenshot({ path: path.join(out, `studio-guest-button-${name}.png`) });
    await page.click('button[aria-label="開啟 Studio AI"]');
    await page.waitForSelector('text=登入後可使用 Studio AI');
    await page.waitForTimeout(400);
    console.log(name, await page.getAttribute('a:has-text("使用 Google 登入")', 'href'));
    await page.screenshot({ path: path.join(out, `studio-guest-${name}.png`) });
    await page.close();
  }
  await browser.close();
})();
