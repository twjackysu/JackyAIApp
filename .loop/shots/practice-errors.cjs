// 第 47 輪：對話練習的失敗提示（D-172）：開始失敗時情境還在＋上方的錯誤；開始後 AI 訊息的語音播不出來時訊息下的提示。
// 用法：cd jackyaiapp.client && npm run build && NODE_PATH=$(npm root -g) node ../.loop/shots/practice-errors.cjs <輸出目錄>
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const dist = path.resolve(__dirname, '../../jackyaiapp.client/dist');
const out = process.argv[2] || '/tmp/shots';
fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.json': 'application/json', '.woff2': 'font/woff2' };
const user = { id: 'u1', name: 'Jacky', email: 'jacky@example.com', isAdmin: false, drawdownBrakeEnabled: false };

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  let starts = 0;
  await page.route('**/*', async (route) => {
    const p = new URL(route.request().url()).pathname;
    const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (p === '/api/account/info') return json({ data: user });
    if (p === '/api/exam/conversation/start') {
      starts += 1;
      if (starts === 1) return json({ error: { code: 1050300, message: 'AI 服務暫時忙碌。' } }, 503);
      return json({ data: { scenario: 'At a café', userRole: 'customer', aiRole: 'barista', firstMessage: 'Hi! What can I get you today?' } });
    }
    if (p.toLowerCase().startsWith('/api/audio/')) return route.fulfill({ status: 429, contentType: 'application/json', body: '{}' });
    if (p.startsWith('/api/')) return json({ data: null });
    const file = path.join(dist, p === '/' || !path.extname(p) ? 'index.html' : p);
    if (!fs.existsSync(file)) return route.fulfill({ status: 404, body: '' });
    return route.fulfill({ body: fs.readFileSync(file), contentType: types[path.extname(file)] || 'text/html' });
  });
  await page.goto('http://localhost:5173/english/exam/conversation');
  await page.locator('.MuiCard-root').first().click();
  await page.getByRole('button', { name: /開始/ }).last().click();
  await page.getByRole('alert').waitFor();
  await page.screenshot({ path: path.join(out, 'practice-start-failed.png') });

  await page.locator('.MuiCard-root').first().click();
  // 再選一次 → 開始成功；AI 的第一句自動播放被拒（429）→ 訊息下的提示。
  await page.getByRole('button', { name: /開始/ }).last().click();
  await page.getByText('Hi! What can I get you today?').waitFor();
  await page.getByRole('button', { name: '播放語音' }).first().click().catch(() => {});
  await page.getByText('語音沒有播出來').waitFor({ timeout: 5000 });
  await page.screenshot({ path: path.join(out, 'practice-playback-failed.png') });
  await browser.close();
})();
