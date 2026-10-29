import { describe, expect, it } from 'vitest';

import { initializeResult, linkOf, stepsFor, toolNotifications } from './appBridge';
import {
  APP_MAX_HEIGHT,
  APP_MIN_HEIGHT,
  appCsp,
  appSrcDoc,
  frameHeight,
  httpsOrigins,
} from './appFrame';
import { appUriOf, parseMcpAppCard, resultText } from './mcpAppCard';

const card = (data: Record<string, unknown>) => ({ kind: 'mcp-app', title: null, data });
const good = {
  installationId: '3f2a-41',
  connector: '天氣',
  tool: 'forecast',
  title: '天氣預報',
  resourceUri: 'ui://weather/forecast.html',
  input: { city: '台北' },
  result: { content: [{ type: 'text', text: '晴' }], structuredContent: { high: 28 } },
};

describe('parseMcpAppCard', () => {
  it('reads a card the server sent', () => {
    expect(parseMcpAppCard(card(good))).toEqual(good);
  });

  it('refuses one without a ui:// app, a plain id, or the call', () => {
    expect(
      parseMcpAppCard(card({ ...good, resourceUri: 'https://evil.example.com/app.html' })),
    ).toBeNull();
    expect(parseMcpAppCard(card({ ...good, installationId: '../../catalog' }))).toBeNull();
    expect(parseMcpAppCard(card({ ...good, result: 'x' }))).toBeNull();
    expect(parseMcpAppCard({ kind: 'chart', title: null, data: good })).toBeNull();
  });

  it('keeps the outside names to one short line', () => {
    const parsed = parseMcpAppCard(
      card({ ...good, connector: 'a\nb'.padEnd(200, 'c'), title: '' }),
    );
    expect(parsed?.connector).toMatch(/^a bc+$/);
    expect(Array.from(parsed!.connector)).toHaveLength(60);
    expect(parsed?.title).toBe('forecast');
  });

  it('drops format characters, so a name cannot reorder the caption around it', () => {
    const parsed = parseMcpAppCard(
      card({ ...good, title: '\u202e天氣\u200b預報', connector: 'a\u2066b' }),
    );
    expect(parsed?.title).toBe('天氣 預報');
    expect(parsed?.connector).toBe('a b');
  });

  it('a ui:// address with white space or controls is not one', () => {
    expect(appUriOf('ui://a b')).toBeNull();
    expect(appUriOf('ui://a\u0000')).toBeNull();
    expect(appUriOf('ui://')).toBeNull();
    expect(appUriOf('ui://ok/x.html')).toBe('ui://ok/x.html');
  });

  it('the text stands in for an app that is not drawn', () => {
    expect(
      resultText({
        content: [{ type: 'text', text: 'a' }, { type: 'image' }, { type: 'text', text: 'b' }],
      }),
    ).toBe('a\nb');
    expect(resultText({})).toBe('');
  });
});

describe('the frame', () => {
  it('has the default CSP with no outside origin', () => {
    const csp = appCsp();
    expect(csp).toContain("default-src 'none'");
    expect(csp).toContain("connect-src 'none'");
    expect(csp).toContain("frame-src 'none'");
    expect(csp).toContain("base-uri 'none'");
    expect(csp).not.toContain('https:');
  });

  it('adds only the https origins the server declared, where it declared them', () => {
    const csp = appCsp(
      ['https://api.example.com'],
      ['https://cdn.example.com:8443', 'http://x.example.com', "https://a.com 'unsafe-eval'"],
    );
    expect(csp).toContain('connect-src https://api.example.com');
    expect(csp).toContain("script-src 'unsafe-inline' https://cdn.example.com:8443");
    expect(csp).not.toContain('http://');
    expect(csp).not.toContain('unsafe-eval');
    expect(httpsOrigins(['https://*.example.com', 'https://ok.example.com/path', 5])).toEqual([]);
    expect(
      httpsOrigins([
        'https://192.168.1.1',
        'https://localhost',
        'https://intranet',
        'https://printer.local',
        'https://a.b.c',
      ]),
    ).toEqual(['https://a.b.c']);
  });

  it('puts our doctype and CSP before anything of the app, dropping only its doctype', () => {
    const doc = appSrcDoc(
      '<!DOCTYPE html><!-- <head> --><html><head><title>t</title></head><body>x</body></html>',
      "default-src 'none'",
    );
    expect(
      doc.startsWith(
        `<!doctype html><meta http-equiv="Content-Security-Policy" content="default-src 'none'">`,
      ),
    ).toBe(true);
    expect(doc.match(/<!doctype/gi)).toHaveLength(1);
    expect(doc.indexOf('Content-Security-Policy')).toBeLessThan(doc.indexOf('<!-- <head> -->'));
  });

  it('escapes the CSP in its attribute', () => {
    expect(appSrcDoc('', 'a"><script>')).toContain('content="a&quot;&gt;&lt;script&gt;"');
  });

  it('keeps an app within the heights it may take', () => {
    expect(frameHeight(10_000)).toBe(APP_MAX_HEIGHT);
    expect(frameHeight(1)).toBe(APP_MIN_HEIGHT);
    expect(frameHeight(300.4)).toBe(300);
    expect(frameHeight('300')).toBeNull();
    expect(frameHeight(Number.NaN)).toBeNull();
  });
});

describe('the bridge', () => {
  const state = { theme: 'dark' as const };

  it('answers ui/initialize with the host and its context', () => {
    const [step] = stepsFor({ jsonrpc: '2.0', id: 1, method: 'ui/initialize', params: {} }, state);
    expect(step).toEqual({
      type: 'reply',
      message: { jsonrpc: '2.0', id: 1, result: initializeResult(state) },
    });
    expect(initializeResult(state).hostContext).toMatchObject({
      theme: 'dark',
      locale: 'zh-TW',
      displayMode: 'inline',
    });
  });

  it('hands over the call only once the app says it is ready', () => {
    expect(stepsFor({ jsonrpc: '2.0', method: 'ui/notifications/initialized' }, state)).toEqual([
      { type: 'ready' },
    ]);
    expect(toolNotifications({ city: '台北' }, good.result).map((n) => n.method)).toEqual([
      'ui/notifications/tool-input',
      'ui/notifications/tool-result',
    ]);
  });

  it('resizes on size-changed', () => {
    expect(
      stepsFor(
        { jsonrpc: '2.0', method: 'ui/notifications/size-changed', params: { height: 320 } },
        state,
      ),
    ).toEqual([{ type: 'resize', height: 320 }]);
  });

  it('offers an https link to the reader rather than opening it, and refuses any other', () => {
    expect(
      stepsFor(
        { jsonrpc: '2.0', id: 2, method: 'ui/open-link', params: { url: 'https://example.com/a' } },
        state,
      ),
    ).toEqual([
      { type: 'offerLink', url: 'https://example.com/a' },
      { type: 'reply', message: { jsonrpc: '2.0', id: 2, result: {} } },
    ]);
    const [refused] = stepsFor(
      { jsonrpc: '2.0', id: 3, method: 'ui/open-link', params: { url: 'javascript:alert(1)' } },
      state,
    );
    expect(refused).toMatchObject({ type: 'reply', message: { id: 3, error: { code: -32602 } } });
    expect(linkOf('https://user:pw@example.com')).toBeNull();
    expect(linkOf('http://example.com')).toBeNull();
  });

  it('does not pass an app’s context to the model, nor run what it does not know', () => {
    expect(
      stepsFor(
        { jsonrpc: '2.0', id: 4, method: 'ui/update-model-context', params: { content: [] } },
        state,
      ),
    ).toEqual([{ type: 'reply', message: { jsonrpc: '2.0', id: 4, result: {} } }]);
    const [unknown] = stepsFor(
      { jsonrpc: '2.0', id: 5, method: 'sampling/createMessage', params: {} },
      state,
    );
    expect(unknown).toMatchObject({ type: 'reply', message: { id: 5, error: { code: -32601 } } });
  });

  it('passes a tools/call on to the server with its name and arguments, and refuses a malformed one', () => {
    expect(
      stepsFor(
        {
          jsonrpc: '2.0',
          id: 6,
          method: 'tools/call',
          params: { name: 'refresh', arguments: { city: '台中' } },
        },
        state,
      ),
    ).toEqual([{ type: 'callTool', id: 6, name: 'refresh', arguments: { city: '台中' } }]);
    expect(
      stepsFor({ jsonrpc: '2.0', id: 7, method: 'tools/call', params: { name: 'refresh' } }, state),
    ).toEqual([{ type: 'callTool', id: 7, name: 'refresh', arguments: {} }]);
    const [bad] = stepsFor(
      { jsonrpc: '2.0', id: 8, method: 'tools/call', params: { name: 'refresh', arguments: [1] } },
      state,
    );
    expect(bad).toMatchObject({ type: 'reply', message: { id: 8, error: { code: -32602 } } });
  });

  it('ignores anything that is not JSON-RPC 2.0', () => {
    expect(stepsFor('hello', state)).toEqual([]);
    expect(stepsFor({ method: 'ui/initialize', id: 1 }, state)).toEqual([]);
    expect(stepsFor({ jsonrpc: '2.0', id: { x: 1 }, method: 'ping' }, state)).toEqual([]);
  });
});
