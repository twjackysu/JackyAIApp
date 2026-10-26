import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import AiConnectorsSection from './AiConnectorsSection';
import { catalog, entry, installed } from './testData';

import type { CatalogEntry } from '@/apis/connectorsApis/types';

import { ASK_STUDIO_EVENT, type AskStudioDetail } from '@/components/StudioAssistant/askStudio';

const { mutation } = vi.hoisted(() => ({
  /** An RTK Query mutation hook whose trigger answers with `fn`'s result (or its throw). */
  mutation: (fn: (...args: unknown[]) => unknown) => () => [
    (...args: unknown[]) => ({ unwrap: () => Promise.resolve(fn(...args)) }),
    { isLoading: false },
  ],
}));

const mocks = vi.hoisted(() => ({
  catalog: vi.fn(),
  create: vi.fn(),
  authorize: vi.fn(),
  test: vi.fn(),
  setEnabled: vi.fn(),
  remove: vi.fn(),
  setPermission: vi.fn(),
  dialog: vi.fn(),
  refetch: vi.fn(),
}));

vi.mock('@/apis/connectorsApis/connectorsApis', () => ({
  useGetConnectorCatalogQuery: () => mocks.catalog(),
  useCreateInstallationMutation: mutation(mocks.create),
  useAuthorizeInstallationMutation: mutation(mocks.authorize),
  useTestInstallationMutation: mutation(mocks.test),
  useSetInstallationEnabledMutation: mutation(mocks.setEnabled),
  useRemoveInstallationMutation: mutation(mocks.remove),
  useSetToolPermissionMutation: mutation(mocks.setPermission),
}));

// The dialog has its own tests; here only whether it opens, and for which entry.
vi.mock('./AddMcpServerDialog', () => ({
  default: (p: { open: boolean; preset: CatalogEntry | null; registrySearch?: string }) => {
    mocks.dialog(p);
    return p.open ? `[Dialog ${p.preset?.key ?? 'custom'}]` : null;
  },
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

/** Where the router is now, so a test sees the address the section left. */
const where = { path: '' };
function Where() {
  const at = useLocation();
  useEffect(() => {
    where.path = at.pathname + at.search;
  });
  return null;
}

function render(data = catalog(), refreshSignal = 0, path = '/connectors') {
  mocks.catalog.mockReturnValue({
    data,
    isLoading: false,
    error: undefined,
    refetch: mocks.refetch,
  });
  act(() =>
    root.render(
      <MemoryRouter initialEntries={[path]}>
        <AiConnectorsSection notify={notify} refreshSignal={refreshSignal} />
        <Where />
      </MemoryRouter>,
    ),
  );
}

const text = () => document.body.textContent ?? '';
const buttons = () => [...document.body.querySelectorAll('button')];
const button = (label: string) => buttons().find((b) => b.textContent === label)!;
const click = async (el: Element) => act(async () => (el as HTMLElement).click());
const openFirstCard = () => click(document.body.querySelector('.MuiCardActionArea-root')!);

describe('AiConnectorsSection', () => {
  it('steps aside, without hiding the page, when the catalog cannot load', () => {
    mocks.catalog.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: { status: 404, data: { error: { code: 1040400, message: 'Not found' } } },
    });
    act(() =>
      root.render(
        <MemoryRouter>
          <AiConnectorsSection notify={notify} />
        </MemoryRouter>,
      ),
    );
    expect(text()).toContain('AI connectors 目前無法載入');
    expect(text()).toContain('帳號連線不受影響');
  });

  it('reads the catalog again when the page asks for fresh status', () => {
    render();
    expect(mocks.refetch).not.toHaveBeenCalled();
    render(catalog(), 1);
    expect(mocks.refetch).toHaveBeenCalledTimes(1);
  });

  it('opens the registry search a link asks for (?registry=), once, and drops it from the address', () => {
    render(catalog(), 0, '/connectors?registry=io.example%2Fweather&x=1');
    expect(text()).toContain('[Dialog custom]');
    expect(mocks.dialog).toHaveBeenLastCalledWith(
      expect.objectContaining({
        open: true,
        browseRegistry: true,
        registrySearch: 'io.example/weather',
      }),
    );
    expect(where.path).toBe('/connectors?x=1');
  });

  it('opens nothing for ?registry= when no more servers of their own can be added', () => {
    const full = Array.from({ length: 2 }, (_, i) =>
      installed({ id: `c${i}`, definitionKey: 'custom', displayName: `S${i}` }),
    );
    render(catalog({ installed: full, customLimit: 2 }), 0, '/connectors?registry=weather');
    expect(text()).not.toContain('[Dialog');
    expect(notify).toHaveBeenCalledWith(expect.stringContaining('最多 2 個'), 'error');
    expect(where.path).toBe('/connectors');
  });

  it('says what to do when nothing is connected yet', () => {
    render();
    expect(text()).toContain('還沒有連線的 connector');
  });

  it("shows each connector's health and how its tools are set", () => {
    render(
      catalog({
        installed: [
          installed({
            displayName: 'Linear',
            health: 'needsAuth',
            tools: [
              { name: 'a', title: null, description: '', effect: 'read', permission: 'auto' },
              { name: 'b', title: null, description: '', effect: 'write', permission: 'confirm' },
            ],
          }),
        ],
      }),
    );
    expect(text()).toContain('Linear');
    expect(text()).toContain('需要重新授權');
    expect(text()).toContain('2 個工具：自動 1・需確認 1');
  });

  it('adds a server without credentials in one click', async () => {
    mocks.create.mockReturnValue(installed());
    render();
    await click(button('連線'));
    expect(mocks.create).toHaveBeenCalledWith({ definitionKey: 'deepwiki', authKind: 'none' });
    expect(notify).toHaveBeenCalledWith('DeepWiki 已連線，AI 現在可以使用它的工具', 'success');
  });

  it('asks for the API key of a catalog entry that needs one', async () => {
    render(catalog({ entries: [entry({ key: 'exa', authKind: 'apiKey' })] }));
    await click(button('連線…'));
    expect(text()).toContain('[Dialog exa]');
    expect(mocks.create).not.toHaveBeenCalled();
  });

  it('adds an OAuth server, then sends the reader to sign in', async () => {
    const assign = vi.fn();
    const original = window.location;
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: {
        set href(url: string) {
          assign(url);
        },
      },
    });
    mocks.create.mockReturnValue(installed({ id: 'i-9', authKind: 'oauth' }));
    mocks.authorize.mockReturnValue({ redirectUrl: 'https://auth.example/authorize' });
    render(catalog({ entries: [entry({ key: 'linear', authKind: 'oauth' })] }));
    await click(button('前往授權'));
    expect(mocks.create).toHaveBeenCalledWith({ definitionKey: 'linear', authKind: 'oauth' });
    expect(mocks.authorize).toHaveBeenCalledWith('i-9');
    expect(assign).toHaveBeenCalledWith('https://auth.example/authorize');
    Object.defineProperty(window, 'location', { configurable: true, value: original });
  });

  it('opens the add dialog empty from the button', async () => {
    render();
    await click(button('新增 MCP server'));
    expect(text()).toContain('[Dialog custom]');
  });

  it('stops adding servers of their own at the limit, and says why', () => {
    render(catalog({ customLimit: 1, installed: [installed({ definitionKey: 'custom' })] }));
    expect(button('新增 MCP server').disabled).toBe(true);
    expect(text()).toContain('自訂 MCP server 最多 1 個');
  });

  it("changes an always-on connector's tool, which has no row yet", async () => {
    const site = installed({
      id: null,
      definitionKey: 'site',
      displayName: '本站工具',
      alwaysOn: true,
      tools: [
        {
          name: 'add_word',
          title: '加入單字',
          description: '',
          effect: 'write',
          permission: 'confirm',
        },
      ],
    });
    mocks.setPermission.mockReturnValue({ ...site, id: 'row-1' });
    render(catalog({ installed: [site] }));
    await openFirstCard();
    expect(text()).toContain('工具（1）');
    // Always-on: nothing to test, disable or remove.
    expect(buttons().some((b) => b.textContent === '移除')).toBe(false);
    await click(button('自動'));
    expect(mocks.setPermission).toHaveBeenCalledWith({
      definitionKey: 'site',
      installationId: null,
      toolName: 'add_word',
      permission: 'auto',
    });
  });

  it('tries a connector in Studio AI from its drawer, in a conversation of its own (D-126)', async () => {
    const asked: AskStudioDetail[] = [];
    const listen = (e: Event) => asked.push((e as CustomEvent<AskStudioDetail>).detail);
    window.addEventListener(ASK_STUDIO_EVENT, listen);
    try {
      render(catalog({ installed: [installed()] }));
      await openFirstCard();
      await click(button('在 Studio AI 試試看'));
    } finally {
      window.removeEventListener(ASK_STUDIO_EVENT, listen);
    }
    expect(asked).toEqual([
      { text: expect.stringContaining('「DeepWiki」'), fresh: true, quotesPageText: true },
    ]);
  });

  it('offers no try for a connector waiting for a sign-in', async () => {
    render(catalog({ installed: [installed({ health: 'needsAuth' })] }));
    await openFirstCard();
    expect(text()).toContain('重新授權');
    expect(button('在 Studio AI 試試看')).toBeUndefined();
  });

  it('asks once more before removing', async () => {
    render(catalog({ installed: [installed()] }));
    await openFirstCard();
    await click(button('移除'));
    expect(mocks.remove).not.toHaveBeenCalled();
    await click(button('確定移除'));
    expect(mocks.remove).toHaveBeenCalledWith('i-1');
    expect(notify).toHaveBeenCalledWith('已移除 DeepWiki', 'success');
  });

  it('reports a failed connection test in words', async () => {
    mocks.test.mockReturnValue({
      ok: false,
      latencyMs: 0,
      error: '401：token 已失效',
      connector: installed(),
    });
    render(catalog({ installed: [installed()] }));
    await openFirstCard();
    await click(button('測試連線'));
    expect(mocks.test).toHaveBeenCalledWith('i-1');
    expect(notify).toHaveBeenCalledWith('連線失敗：401：token 已失效', 'error');
  });
});
