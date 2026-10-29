import { ThemeProvider, createTheme } from '@mui/material/styles';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { HOST_CONTEXT_CHANGED } from './appBridge';
import McpAppCardView from './McpAppCardView';

import type { Root } from 'react-dom/client';

// A change of light or dark is told to the app (host-context-changed), never a new document: the app keeps its state (D-210).
vi.mock('@/apis/connectorsApis/connectorsApis', () => ({
  useGetMcpAppQuery: () => ({
    data: {
      html: '<html><body>app</body></html>',
      connectDomains: [],
      resourceDomains: [],
      prefersBorder: null,
      connector: '天氣',
      host: 'mcp.weather.example.com',
    },
    isLoading: false,
  }),
  useCallFromMcpAppMutation: () => [vi.fn()],
}));

const card = {
  kind: 'mcp-app',
  title: null,
  data: {
    installationId: 'i1',
    connector: '天氣',
    tool: 'forecast',
    title: '天氣預報',
    resourceUri: 'ui://weather/forecast.html',
    input: { city: '台北' },
    result: { content: [{ type: 'text', text: '台北明天晴' }] },
  },
};

const themes = {
  light: createTheme({ palette: { mode: 'light' } }),
  dark: createTheme({ palette: { mode: 'dark' } }),
};

let container: HTMLDivElement | null = null;
let root: Root | null = null;
afterEach(() => {
  act(() => root?.unmount());
  container?.remove();
  container = null;
  root = null;
});

function mount() {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  const show = (mode: 'light' | 'dark') =>
    act(() =>
      root!.render(
        <ThemeProvider theme={themes[mode]}>
          <McpAppCardView card={card} />
        </ThemeProvider>,
      ),
    );
  show('light');
  const frame = container.querySelector('iframe')!;
  const sent: unknown[] = [];
  vi.spyOn(frame.contentWindow!, 'postMessage').mockImplementation((m: unknown) => {
    sent.push(m);
  });
  const fromApp = (data: unknown) =>
    act(() => {
      window.dispatchEvent(new MessageEvent('message', { data, source: frame.contentWindow }));
    });
  const told = () => sent.filter((m) => (m as { method?: string }).method === HOST_CONTEXT_CHANGED);
  return { frame, show, fromApp, told, sent };
}

const initialized = { jsonrpc: '2.0', method: 'ui/notifications/initialized' };

describe('an MCP App through a change of light or dark', () => {
  it('keeps the document and tells the app the new theme', () => {
    const { frame, show, fromApp, told } = mount();
    const doc = frame.getAttribute('srcdoc');
    expect(doc).toContain('<meta name="color-scheme" content="light">');
    fromApp(initialized);

    show('dark');

    // The same frame and document: nothing drawn again, so the app's state stays.
    expect(container!.querySelector('iframe')).toBe(frame);
    expect(frame.getAttribute('srcdoc')).toBe(doc);
    expect(told()).toEqual([
      { jsonrpc: '2.0', method: HOST_CONTEXT_CHANGED, params: { theme: 'dark' } },
    ]);

    show('light');
    expect(told().map((m) => (m as { params: { theme: string } }).params.theme)).toEqual([
      'dark',
      'light',
    ]);
  });

  it('tells nothing before the app is ready, then the theme its document does not have yet', () => {
    const { show, fromApp, told, sent } = mount();

    show('dark');
    expect(sent).toEqual([]);

    // ui/initialize already says dark; the document's scheme is still light until the app is told.
    fromApp({ jsonrpc: '2.0', id: 1, method: 'ui/initialize', params: {} });
    expect(sent[0]).toMatchObject({ id: 1, result: { hostContext: { theme: 'dark' } } });
    fromApp(initialized);

    expect(told()).toEqual([
      { jsonrpc: '2.0', method: HOST_CONTEXT_CHANGED, params: { theme: 'dark' } },
    ]);
    // The tool's input and result come first, as on any start.
    expect(sent.map((m) => (m as { method?: string }).method).slice(1)).toEqual([
      'ui/notifications/tool-input',
      'ui/notifications/tool-result',
      HOST_CONTEXT_CHANGED,
    ]);
  });

  it('tells nothing when the theme did not change', () => {
    const { show, fromApp, told } = mount();
    fromApp(initialized);

    show('light');
    fromApp(initialized);

    expect(told()).toEqual([]);
  });
});
