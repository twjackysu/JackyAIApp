// 本站 MCP App（SiteMcpApp.Html）的截圖（迴圈專用）：自己扮 MCP Apps 的 host——ui/initialize → tool-result → size-changed。
// 用法：NODE_PATH=$(npm root -g) node .loop/shots/mcp-app.cjs <輸出目錄>
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const out = process.argv[2] || '/tmp/shots';
fs.mkdirSync(out, { recursive: true });
const source = fs.readFileSync(path.resolve(__dirname, '../../JackyAIApp.Server/Services/Connectors/SiteMcp/SiteMcpApp.cs'), 'utf8');
const html = source.match(/Html = """\n([\s\S]*?)\n {12}""";/)[1].split('\n').map((l) => l.replace(/^ {12}/, '')).join('\n');

const results = [
  { isError: false, structuredContent: { card: { kind: 'table', title: '投資組合　淨資產 3,250,000 TWD',
    columns: [{ key: 'name', label: '持倉' }, { key: 'value', label: '市值', numeric: true }, { key: 'pnl', label: '損益', numeric: true }, { key: 'pnlPct', label: '損益 %', numeric: true }],
    rows: [
      { name: '台積電（2330）', value: 1820000, pnl: 412000, pnlPct: 29.26 },
      { name: '元大台灣50（0050）', value: 860000, pnl: 95000, pnlPct: 12.42 },
      { name: '<img src=x onerror=alert(1)>', value: 320000, pnl: -18000, pnlPct: -5.33 },
      { name: '現金', value: 250000, pnl: null, pnlPct: null },
    ] } } },
  { isError: false, structuredContent: { card: { kind: 'link', title: 'serendipity /ˌsɛrənˈdɪpəti/', description: '意外發現珍奇事物的本領',
    href: 'https://apps.jacky-studio.com/english/dictionary/serendipity', actionLabel: '看完整解釋' } } },
];

(async () => {
  const browser = await chromium.launch();
  for (const theme of ['light', 'dark']) {
    const page = await browser.newPage({ viewport: { width: 640, height: 480 } });
    page.on('dialog', (d) => { console.log('DIALOG (should never happen):', d.message()); d.dismiss(); });
    await page.setContent(`<body style="background:${theme === 'dark' ? '#111' : '#fff'};padding:20px">
      <iframe id="f0" sandbox="allow-scripts" style="width:560px;border:1px solid #888;border-radius:8px;display:block;margin-bottom:16px" height="40"></iframe>
      <iframe id="f1" sandbox="allow-scripts" style="width:560px;border:1px solid #888;border-radius:8px" height="40"></iframe></body>`);
    await page.evaluate(({ html, results, theme }) => {
      window.opened = [];
      results.forEach((result, i) => {
        const f = document.getElementById('f' + i);
        window.addEventListener('message', (e) => {
          if (e.source !== f.contentWindow) return;
          const m = e.data;
          const reply = (r) => f.contentWindow.postMessage({ jsonrpc: '2.0', id: m.id, result: r }, '*');
          if (m.method === 'ui/initialize') reply({ protocolVersion: '2026-01-26', hostInfo: { name: 'test' }, hostCapabilities: {}, hostContext: { theme } });
          else if (m.method === 'ui/notifications/initialized') f.contentWindow.postMessage({ jsonrpc: '2.0', method: 'ui/notifications/tool-result', params: result }, '*');
          else if (m.method === 'ui/notifications/size-changed') f.height = m.params.height;
          else if (m.method === 'ui/open-link') { window.opened.push(m.params.url); reply({}); }
        });
        f.srcdoc = html;
      });
    }, { html, results, theme });
    await page.waitForTimeout(800);
    await page.screenshot({ path: `${out}/mcp-app-card-${theme}.png` });
    await page.frames()[2].click('button');
    await page.waitForTimeout(200);
    console.log(theme, 'heights', await page.evaluate(() => [f0.height, f1.height]), 'opened', await page.evaluate(() => window.opened));
    await page.close();
  }
  await browser.close();
})();
