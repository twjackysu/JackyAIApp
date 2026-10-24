import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import SiteMcpSection from './index';

import type { SiteAccessToken, SiteAccessTokenList } from '@/apis/connectorsApis/types';

const mocks = vi.hoisted(() => ({
  tokens: vi.fn(),
  create: vi.fn(),
  revoke: vi.fn(),
  copy: vi.fn(),
}));

const { mutation } = vi.hoisted(() => ({
  /** An RTK Query mutation hook whose trigger answers with `fn`'s result (or its throw). */
  mutation: (fn: (...args: unknown[]) => unknown) => () => [
    (...args: unknown[]) => ({ unwrap: () => Promise.resolve().then(() => fn(...args)) }),
    { isLoading: false },
  ],
}));

vi.mock('@/apis/connectorsApis/connectorsApis', () => ({
  useGetSiteTokensQuery: () => mocks.tokens(),
  useCreateSiteTokenMutation: mutation(mocks.create),
  useRevokeSiteTokenMutation: mutation(mocks.revoke),
}));

vi.mock('@/components/aiChat/utils/clipboard', () => ({
  copyText: (text: string) => mocks.copy(text),
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;
const notify = vi.fn();

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.clearAllMocks();
});

const token = (over: Partial<SiteAccessToken> = {}): SiteAccessToken => ({
  id: 't1',
  name: 'Claude Desktop',
  hint: 'jsk_AbC…xYz9',
  allowWrites: false,
  viaOAuth: false,
  createdAt: '2026-10-24T09:00:00',
  expiresAt: '2099-01-22T09:00:00',
  lastUsedAt: null,
  ...over,
});

function render(
  data: SiteAccessTokenList | undefined = { tokens: [], limit: 10 },
  error?: unknown,
) {
  mocks.tokens.mockReturnValue({ data, isLoading: false, error });
  act(() => root.render(<SiteMcpSection notify={notify} />));
}

const text = () => document.body.textContent ?? '';
const button = (label: string) =>
  [...document.body.querySelectorAll('button')].find((b) => b.textContent === label)!;
const click = async (el: Element) => act(async () => (el as HTMLElement).click());

describe('SiteMcpSection', () => {
  it("shows the site's MCP address and that there is no token yet", () => {
    render();
    expect(text()).toContain('讓其他 AI 使用本站');
    expect(text()).toContain(`${window.location.origin}/api/mcp`);
    expect(text()).toContain('還沒有 token');
  });

  it('lists the tokens without the token itself, read-only or not', () => {
    render({
      tokens: [
        token(),
        token({ id: 't2', name: 'Cursor', allowWrites: true, lastUsedAt: '2026-10-20T03:00:00' }),
      ],
      limit: 10,
    });
    expect(text()).not.toContain('經 OAuth 登入');
    expect(text()).toContain('Claude Desktop');
    expect(text()).toContain('jsk_AbC…xYz9');
    expect(text()).toContain('唯讀');
    expect(text()).toContain('可寫入');
    expect(text()).toContain('從未使用');
    expect(text()).toContain('最後使用 2026/10/20');
  });

  it('marks a token another AI got by signing in with OAuth', () => {
    render({ tokens: [token({ name: 'Claude', viaOAuth: true })], limit: 10 });
    expect(text()).toContain('Claude');
    expect(text()).toContain('經 OAuth 登入');
  });

  it('makes no more tokens at the limit', () => {
    render({ tokens: [token()], limit: 1 });
    expect(button('產生 token').disabled).toBe(true);
    expect(text()).toContain('最多 1 個 token');
  });

  it('steps aside when the list cannot load', () => {
    render(undefined, { status: 500, data: { error: { code: 1050000, message: 'Boom' } } });
    expect(text()).toContain('token 清單目前無法載入');
  });

  it('revokes a token only once asked twice', async () => {
    render({ tokens: [token()], limit: 10 });
    await click(button('撤銷'));
    expect(mocks.revoke).not.toHaveBeenCalled();
    await click(button('確定撤銷'));
    expect(mocks.revoke).toHaveBeenCalledWith('t1');
    expect(notify).toHaveBeenCalledWith(
      '已撤銷「Claude Desktop」，用它的 AI 從現在起連不上',
      'success',
    );
  });

  it('makes a token, shows it once with the settings for each AI, and forgets it on close', async () => {
    mocks.create.mockReturnValue({ info: token({ name: 'My Cursor' }), token: 'jsk_secret123' });
    render();
    await click(button('產生 token'));

    const name = document.body.querySelector<HTMLInputElement>('input[maxlength="50"]')!;
    await act(async () => {
      const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
      setValue.call(name, 'My Cursor');
      name.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await click(document.body.querySelector('input[type="checkbox"]')!);
    await click(button('產生'));

    expect(mocks.create).toHaveBeenCalledWith({
      name: 'My Cursor',
      allowWrites: true,
      expiresInDays: 90,
    });
    expect(text()).toContain('唯一一次看得到這個 token');
    expect(text()).toContain('jsk_secret123');
    expect(text()).toContain('claude mcp add --transport http jacky-studio');

    await click(document.body.querySelector('button[aria-label="複製 token"]')!);
    expect(mocks.copy).toHaveBeenCalledWith('jsk_secret123');

    await click(
      [...document.body.querySelectorAll('[role="tab"]')].find((t) => t.textContent === 'Cursor')!,
    );
    expect(text()).toContain('~/.cursor/mcp.json');
    expect(text()).toContain('"Authorization": "Bearer jsk_secret123"');

    await click(button('我已經複製好了'));
    expect(text()).not.toContain('jsk_secret123');
  });

  it('says why a token was not made', async () => {
    mocks.create.mockImplementation(() => {
      throw { status: 400, data: { error: { code: 1040000, message: '最多只能有 10 個 token' } } };
    });
    render();
    await click(button('產生 token'));
    await click(button('產生'));
    expect(text()).toContain('最多只能有 10 個 token');
  });
});
