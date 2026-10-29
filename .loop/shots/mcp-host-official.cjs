// 第 54 輪（D-203、D-204）：用 **MCP 官方的範例 app**（@modelcontextprotocol/server-basic-vanillajs，官方 ext-apps SDK 的 `App`）
// 驗證本站的 host：ui/initialize 的回答過得了官方 SDK 的 schema、拿到 tool-result 畫出時間、「Get Server Time」經 app-call、
// 「Send Message」（ui/message）→ 卡片下方「放進輸入框」→ 進到輸入框、「Open Link」要讀者按。
// 準備（官方 server 在本機跑，拿它的 HTML 與一次呼叫的結果）：
//   mkdir -p /tmp/extapp && cd /tmp/extapp && npm i @modelcontextprotocol/server-basic-vanillajs@2.0.4
//   PORT=3011 node node_modules/.bin/mcp-server-basic-vanillajs &
//   H=(-H Content-Type:application/json -H Accept:application/json,text/event-stream)
//   curl -s --noproxy localhost -X POST http://localhost:3011/mcp "${H[@]}" -d '{"jsonrpc":"2.0","id":3,"method":"resources/read","params":{"uri":"ui://get-time/mcp-app.html"}}' | sed -n 's/^data: //p' > read.json
//   curl -s --noproxy localhost -X POST http://localhost:3011/mcp "${H[@]}" -d '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get-time","arguments":{}}}' | sed -n 's/^data: //p' > call.json
// 用法：cd jackyaiapp.client && npm run build && NODE_PATH=$(npm root -g) node ../.loop/shots/mcp-host-official.cjs <輸出目錄> read.json call.json
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const dist = path.resolve(__dirname, '../../jackyaiapp.client/dist');
const out = process.argv[2] || '/tmp/shots';
fs.mkdirSync(out, { recursive: true });
const types = { '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.json': 'application/json', '.woff2': 'font/woff2' };
const user = { id: 'u1', name: 'Jacky', email: 'jacky@example.com', isAdmin: false, drawdownBrakeEnabled: false };

const read = JSON.parse(fs.readFileSync(process.argv[3], 'utf8')).result.contents[0];
const call = JSON.parse(fs.readFileSync(process.argv[4], 'utf8')).result;

const fakeChat = (events) => {
  const realFetch = window.fetch.bind(window);
  window.fetch = (input, init) => {
    const url = typeof input === 'string' ? input : input.url;
    if (!url.includes('/api/chatbot/chat')) return realFetch(input, init);
    const enc = new TextEncoder();
    const body = new ReadableStream({
      async start(controller) {
        for (const e of events) {
          await new Promise((r) => setTimeout(r, 150));
          controller.enqueue(enc.encode(`data: ${JSON.stringify(e)}\n\n`));
        }
        controller.close();
      },
    });
    return Promise.resolve(new Response(body, { headers: { 'Content-Type': 'text/event-stream' } }));
  };
};

const events = [
  { type: 'start', conversationId: 'c1', userMessageId: 1 },
  { type: 'tool_use', id: 't1', name: 'm1__get-time', displayName: 'Basic App・Get Time' },
  {
    type: 'tool_result', id: 't1', name: 'm1__get-time', ok: true, durationMs: 80,
    card: { kind: 'mcp-app', data: { installationId: 'i-1', connector: 'Basic MCP App', tool: 'get-time', title: 'Get Time', resourceUri: read.uri, input: {}, result: call } },
  },
  { type: 'delta', text: '這是 server 回報的時間。' },
  { type: 'done', conversationId: 'c1', assistantMessageId: 2 },
];

(async () => {
  const browser = await chromium.launch();
  for (const [name, viewport] of [['desktop', { width: 1280, height: 900 }], ['phone', { width: 390, height: 844 }]]) {
    const page = await browser.newPage({ viewport });
    page.on('console', (m) => { if (m.type() === 'error' || /Host|initialize|message|link/i.test(m.text())) console.log(name, 'console:', m.text().slice(0, 200)); });
    let opened = null;
    await page.exposeFunction('noteOpen', (u) => { opened = u; });
    await page.addInitScript(() => { window.open = (u) => { window.noteOpen(String(u)); return null; }; });
    await page.addInitScript(fakeChat, events);
    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      const p = url.pathname;
      const json = (body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
      if (p === '/api/account/info') return json({ data: user });
      if (p === '/api/connectors/installations/i-1/app-call') {
        const body = JSON.parse(route.request().postData() || '{}');
        console.log(name, 'app-call', body.tool);
        const t = '2026-11-05T04:00:00.000Z';
        return json({ data: { result: { content: [{ type: 'text', text: t }], structuredContent: { time: t } } } });
      }
      if (p === '/api/connectors/installations/i-1/app') {
        return json({ data: { html: read.text, connectDomains: [], resourceDomains: [], prefersBorder: null, connector: 'Basic MCP App', host: 'basic-app.example.com' } });
      }
      if (p.startsWith('/api/chatbot/conversations')) return json({ data: [] });
      if (p.startsWith('/api/')) return json({ data: null });
      const file = path.join(dist, p === '/' || !path.extname(p) ? 'index.html' : p);
      if (!fs.existsSync(file)) return route.fulfill({ status: 404, body: '' });
      return route.fulfill({ body: fs.readFileSync(file), contentType: types[path.extname(file)] || 'text/html' });
    });
    await page.goto('http://localhost:5173/');
    await page.waitForTimeout(1500);
    await page.keyboard.press('Control+k');
    await page.waitForTimeout(1200);
    const box = page.getByRole('textbox').last();
    await box.fill('現在 server 那邊幾點？');
    await box.press('Enter');
    await page.waitForTimeout(5000);
    const frame = page.frameLocator('iframe[title^="Get Time"]');
    console.log(name, 'server time shown:', await frame.locator('#server-time').textContent().catch((e) => 'ERR ' + e.message));
    await page.screenshot({ path: path.join(out, `official-app-${name}.png`) });
    await frame.locator('#get-time-btn').click();
    await page.waitForTimeout(800);
    console.log(name, 'after Get Server Time:', await frame.locator('#server-time').textContent().catch((e) => 'ERR ' + e.message));
    await frame.locator('#message-text').fill('幫我把這個時間換成台北時間');
    await frame.locator('#send-message-btn').click();
    await page.waitForTimeout(600);
    await page.locator('[data-testid="mcp-app-card"]').scrollIntoViewIfNeeded();
    await page.screenshot({ path: path.join(out, `official-app-message-${name}.png`) });
    await page.getByRole('button', { name: '放進輸入框' }).click();
    await page.waitForTimeout(300);
    console.log(name, 'box now:', JSON.stringify(await page.getByRole('textbox').last().inputValue()));
    await page.screenshot({ path: path.join(out, `official-app-draft-${name}.png`) });
    await frame.locator('#open-link-btn').click();
    await page.waitForTimeout(500);
    const offered = await page.getByText('介面想開啟').count();
    if (offered) await page.getByRole('button', { name: '開啟' }).click();
    await page.waitForTimeout(300);
    console.log(name, 'link offered:', offered, 'opened:', opened);
    await page.close();
  }
  await browser.close();
})();
