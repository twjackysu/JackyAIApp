import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import McpServersCardView from './McpServersCardView';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

const render = (data: Record<string, unknown>) =>
  act(() =>
    root.render(
      <MemoryRouter>
        <McpServersCardView card={{ kind: 'mcp-servers', title: null, data }} />
      </MemoryRouter>,
    ),
  );

const links = () =>
  [...container.querySelectorAll('a')].map((a) => [a.textContent, a.getAttribute('href')]);

describe('McpServersCardView', () => {
  it('points each server at /connectors, searching for exactly it', () => {
    render({
      search: 'weather',
      more: false,
      servers: [
        {
          name: 'io.example/weather',
          title: 'Weather <b>now</b>',
          host: 'weather.example.com',
          needs: '需要 API key（X-API-Key）',
        },
        { name: 'com.deepwiki/mcp', title: 'DeepWiki', inCatalog: true },
      ],
    });
    const text = container.textContent ?? '';
    expect(text).toContain('Weather <b>now</b>'); // plain text, never markup
    expect(container.querySelector('b')).toBeNull();
    expect(text).toContain('weather.example.com');
    expect(text).toContain('需要 API key（X-API-Key）');
    expect(links()).toEqual([
      ['加入', '/connectors?registry=io.example%2Fweather'],
      ['從目錄連線', '/connectors'],
      ['在 Connectors 搜尋', '/connectors?registry=weather'],
    ]);
  });

  it('offers every result when there were more than shown', () => {
    render({ search: 'git', more: true, servers: [{ name: 'io.example/git' }] });
    expect(links()[links().length - 1]).toEqual(['看全部結果', '/connectors?registry=git']);
  });

  it('draws nothing for a card that does not add up', () => {
    render({ search: 'x', servers: [] });
    expect(container.textContent).toBe('');
  });
});
