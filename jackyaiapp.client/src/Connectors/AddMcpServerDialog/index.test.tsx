import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { entry, installed, probe, registryServer } from '../testData';

import AddMcpServerDialog from './index';

import { ASK_STUDIO_EVENT, type AskStudioDetail } from '@/components/StudioAssistant/askStudio';

const { mutation } = vi.hoisted(() => ({
  /** An RTK Query mutation hook whose trigger answers with `fn`'s result (or its throw). */
  mutation: (fn: (...args: unknown[]) => unknown) => () => [
    (...args: unknown[]) => ({
      unwrap: () => {
        try {
          return Promise.resolve(fn(...args));
        } catch (e) {
          return Promise.reject(e);
        }
      },
    }),
    { isLoading: false },
  ],
}));

const mocks = vi.hoisted(() => ({
  probe: vi.fn(),
  create: vi.fn(),
  setPermission: vi.fn(),
  registry: vi.fn(),
}));

vi.mock('@/apis/connectorsApis/connectorsApis', () => ({
  useProbeMcpServerMutation: mutation(mocks.probe),
  useCreateInstallationMutation: mutation(mocks.create),
  useSetToolPermissionMutation: mutation(mocks.setPermission),
  useSearchMcpRegistryQuery: (search: string) => mocks.registry(search),
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;
const onClose = vi.fn();
const onAuthorize = vi.fn();

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

function render(
  preset = null as ReturnType<typeof entry> | null,
  browseRegistry = false,
  registrySearch?: string,
  installedUrls?: ReadonlySet<string>,
) {
  act(() =>
    root.render(
      <AddMcpServerDialog
        open
        preset={preset}
        browseRegistry={browseRegistry}
        registrySearch={registrySearch}
        installedUrls={installedUrls}
        onClose={onClose}
        onAuthorize={onAuthorize}
      />,
    ),
  );
}

const text = () => document.body.textContent ?? '';
const button = (label: string) =>
  [...document.body.querySelectorAll('button')].find((b) => b.textContent === label)!;
const click = async (label: string) => act(async () => button(label).click());
const input = (label: string) =>
  [...document.body.querySelectorAll('label')]
    .filter((l) => l.textContent?.startsWith(label))
    .map((l) => document.getElementById(l.htmlFor) as HTMLInputElement)[0];

/** Types into a controlled input the way React notices. */
function type(el: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
  act(() => {
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

describe('AddMcpServerDialog', () => {
  it('catches a URL that is not https before asking the server', async () => {
    render();
    type(input('MCP server 網址'), 'http://mcp.example.com');
    await click('檢查');
    expect(text()).toContain('MCP server 必須使用 https');
    expect(mocks.probe).not.toHaveBeenCalled();
  });

  it("shows the server's reason when it refuses the URL", async () => {
    mocks.probe.mockImplementation(() => {
      throw { status: 400, data: { error: { code: 1400000, message: '不能連到內部網路位址' } } };
    });
    render();
    type(input('MCP server 網址'), 'https://10.0.0.1/mcp');
    await click('檢查');
    expect(text()).toContain('不能連到內部網路位址');
  });

  it('adds a server without sign-in and shows its tools with their permissions', async () => {
    mocks.probe.mockReturnValue(probe({ serverName: 'DeepWiki' }));
    mocks.create.mockReturnValue(installed());
    render();
    type(input('MCP server 網址'), 'https://mcp.example.com/mcp');
    await click('檢查');
    expect(mocks.probe).toHaveBeenCalledWith({ serverUrl: 'https://mcp.example.com/mcp' });
    expect(text()).toContain('不需要登入');
    expect(input('名稱').value).toBe('DeepWiki');
    await click('連線');
    expect(mocks.create).toHaveBeenCalledWith({
      definitionKey: 'custom',
      displayName: 'DeepWiki',
      serverUrl: 'https://mcp.example.com/mcp',
      authKind: 'none',
    });
    expect(text()).toContain('DeepWiki 已連線，有 1 個工具');
    mocks.setPermission.mockReturnValue(installed());
    await click('需確認');
    expect(mocks.setPermission).toHaveBeenCalledWith({
      definitionKey: 'deepwiki',
      installationId: 'i-1',
      toolName: 'ask_question',
      permission: 'confirm',
    });
  });

  it('offers to try a server just added in Studio AI, closing first (D-126)', async () => {
    mocks.probe.mockReturnValue(probe({ serverName: 'DeepWiki' }));
    mocks.create.mockReturnValue(installed());
    const asked: AskStudioDetail[] = [];
    const listen = (e: Event) => asked.push((e as CustomEvent<AskStudioDetail>).detail);
    window.addEventListener(ASK_STUDIO_EVENT, listen);
    try {
      render();
      type(input('MCP server 網址'), 'https://mcp.example.com/mcp');
      await click('檢查');
      await click('連線');
      await click('在 Studio AI 試試看');
    } finally {
      window.removeEventListener(ASK_STUDIO_EVENT, listen);
    }
    expect(onClose).toHaveBeenCalled();
    expect(asked).toHaveLength(1);
    expect(asked[0].fresh).toBe(true);
    expect(asked[0].quotesPageText).toBe(true);
    expect(asked[0].text).toContain('「DeepWiki」');
  });

  it('does not offer to try a server whose tools the AI cannot see', async () => {
    mocks.probe.mockReturnValue(probe({ serverName: 'Empty' }));
    mocks.create.mockReturnValue(installed({ tools: [] }));
    render();
    type(input('MCP server 網址'), 'https://mcp.example.com/mcp');
    await click('檢查');
    await click('連線');
    expect(text()).toContain('已連線');
    expect(button('在 Studio AI 試試看')).toBeUndefined();
  });

  it('waits for the token of a server that needs one', async () => {
    mocks.probe.mockReturnValue(probe({ auth: 'apiKey' }));
    render();
    type(input('MCP server 網址'), 'https://mcp.example.com/mcp');
    await click('檢查');
    expect(button('連線').disabled).toBe(true);
    expect(text()).toContain('請填入 token');
    type(input('Token'), 'secret');
    expect(button('連線').disabled).toBe(false);
  });

  it('never sends a key typed for one server to another one checked after it', async () => {
    mocks.probe.mockReturnValue(probe({ auth: 'apiKey' }));
    render();
    type(input('MCP server 網址'), 'https://a.example.com/mcp');
    await click('檢查');
    type(input('Token'), 'secret-for-a');
    await click('上一步');
    type(input('MCP server 網址'), 'https://b.example.com/mcp');
    await click('檢查');

    expect(input('Token').value).toBe('');
    expect(button('連線').disabled).toBe(true);
  });

  it('keeps the key when the same server is checked again', async () => {
    mocks.probe.mockReturnValue(probe({ auth: 'apiKey' }));
    render();
    type(input('MCP server 網址'), 'https://a.example.com/mcp');
    await click('檢查');
    type(input('Token'), 'secret-for-a');
    await click('上一步');
    await click('檢查');

    expect(input('Token').value).toBe('secret-for-a');
  });

  it('hands an OAuth server to the page to sign in, instead of finishing', async () => {
    mocks.probe.mockReturnValue(probe({ auth: 'oauth', registration: 'cimd' }));
    const added = installed({ authKind: 'oauth' });
    mocks.create.mockReturnValue(added);
    render();
    type(input('MCP server 網址'), 'https://mcp.example.com/mcp');
    await click('檢查');
    await click('前往授權');
    expect(onAuthorize).toHaveBeenCalledWith(added);
  });

  it('shows the redirect URI to register when the reader has to make the OAuth app by hand', async () => {
    const redirectUri = 'https://apps.example.com/api/connectors/callback/mcp';
    mocks.probe.mockReturnValue(probe({ auth: 'oauth', registration: 'manual', redirectUri }));
    render();
    type(input('MCP server 網址'), 'https://mcp.example.com/mcp');
    await click('檢查');
    expect(text()).toContain(redirectUri);
    expect(document.body.querySelector('button[aria-label="複製 Redirect URI"]')).not.toBeNull();
  });

  it('shows no redirect URI when the server registers us itself', async () => {
    mocks.probe.mockReturnValue(probe({ auth: 'oauth', registration: 'dcr' }));
    render();
    type(input('MCP server 網址'), 'https://mcp.example.com/mcp');
    await click('檢查');
    expect(text()).not.toContain('Redirect URI');
  });

  it('asks a catalog entry only for its key, under its own definition', async () => {
    mocks.create.mockReturnValue(installed({ definitionKey: 'exa' }));
    render(entry({ key: 'exa', displayName: 'Exa', authKind: 'apiKey' }));
    expect(text()).toContain('連線 Exa');
    expect(text()).not.toContain('上一步');
    type(input('Token'), 'k');
    await click('連線');
    expect(mocks.create).toHaveBeenCalledWith({
      definitionKey: 'exa',
      displayName: 'Exa',
      serverUrl: undefined,
      authKind: 'bearer',
      credentials: { headerName: 'Authorization', value: 'k' },
    });
  });
  it('keeps the registry folded until asked, so opening the dialog reads nothing', () => {
    render();
    expect(mocks.registry).not.toHaveBeenCalled();
    expect(text()).toContain('從 MCP 官方目錄找');
  });

  it('starts the registry search with what a link asked for (Studio AI, D-124)', () => {
    mocks.registry.mockReturnValue({
      currentData: { servers: [], more: false },
      isFetching: false,
    });
    render(null, true, 'io.example/weather');
    expect(mocks.registry).toHaveBeenCalledWith('io.example/weather');
    expect(
      (document.body.querySelector('input[placeholder^="例如"]') as HTMLInputElement).value,
    ).toBe('io.example/weather');
  });

  it('starts over with a new search asked for while the dialog is open', () => {
    mocks.registry.mockReturnValue({
      currentData: { servers: [], more: false },
      isFetching: false,
    });
    render(null, true, 'io.example/weather');
    render(null, true, 'io.example/maps');
    expect(mocks.registry).toHaveBeenLastCalledWith('io.example/maps');
    expect(
      (document.body.querySelector('input[placeholder^="例如"]') as HTMLInputElement).value,
    ).toBe('io.example/maps');
  });

  it('shows a registry server the reader has already added as added, not offered again (D-127)', () => {
    mocks.registry.mockReturnValue({
      currentData: {
        servers: [
          registryServer({
            title: 'Weather',
            url: 'https://mcp.weather.example/mcp/',
            catalogKey: 'weather',
          }),
          registryServer({
            name: 'io.example/news',
            title: 'News',
            url: 'https://news.example/mcp',
          }),
        ],
        more: false,
      },
      isFetching: false,
    });
    render(null, true, 'w', new Set(['https://mcp.weather.example/mcp']));
    expect(text()).toContain('已經加入了'); // added wins over the catalog's note, as on Studio's card
    expect(text()).not.toContain('上方目錄已有');
    expect(document.body.querySelector('[aria-label="使用 Weather"]')).toBeNull();
    expect(document.body.querySelector('[aria-label="使用 News"]')).not.toBeNull();
  });

  it('checks a server picked from the registry, named by its title, with its header ready', async () => {
    mocks.registry.mockReturnValue({
      currentData: {
        servers: [
          registryServer({ title: 'Weather', requiredHeaders: ['X-API-Key'] }),
          registryServer({
            title: 'DeepWiki',
            url: 'https://mcp.deepwiki.com/mcp',
            catalogKey: 'deepwiki',
          }),
        ],
        more: false,
      },
      error: undefined,
      isFetching: false,
    });
    mocks.probe.mockReturnValue(
      probe({ serverUrl: 'https://mcp.weather.example/mcp', auth: 'apiKey' }),
    );
    render(null, true);
    expect(text()).toContain('需要 API key（X-API-Key）');
    // One the catalog already has is connected from there, not added a second time.
    expect(text()).toContain('上方目錄已有');
    expect(button('使用')).toBeTruthy();
    await act(async () =>
      (document.body.querySelector('[aria-label="使用 Weather"]') as HTMLButtonElement).click(),
    );
    expect(mocks.probe).toHaveBeenCalledWith({ serverUrl: 'https://mcp.weather.example/mcp' });
    expect(input('名稱').value).toBe('Weather');
    expect(input('Header 名稱').value).toBe('X-API-Key');
    type(input('API key'), 'k');
    mocks.create.mockReturnValue(installed());
    await click('連線');
    expect(mocks.create).toHaveBeenCalledWith({
      definitionKey: 'custom',
      displayName: 'Weather',
      serverUrl: 'https://mcp.weather.example/mcp',
      authKind: 'apiKey',
      credentials: { headerName: 'X-API-Key', value: 'k' },
    });
  });

  it('says the registry could not be read, and the URL can still be pasted', () => {
    mocks.registry.mockReturnValue({
      currentData: undefined,
      error: { status: 502, data: { error: { code: 1050200, message: 'MCP 官方目錄暫時讀不到' } } },
      isFetching: false,
    });
    render(null, true);
    expect(text()).toContain('MCP 官方目錄暫時讀不到');
    expect(input('MCP server 網址')).toBeTruthy();
  });
  it("starts each pick from an empty form, so one server's header does not carry to the next", async () => {
    mocks.registry.mockReturnValue({
      currentData: {
        servers: [
          registryServer({
            title: 'Keyed',
            url: 'https://k.example/mcp',
            requiredHeaders: ['X-Key'],
          }),
          registryServer({ title: 'Plain', url: 'https://p.example/mcp' }),
        ],
        more: false,
      },
      error: undefined,
      isFetching: false,
    });
    mocks.probe.mockReturnValue(probe({ auth: 'apiKey' }));
    render(null, true);
    const pick = (title: string) =>
      act(async () =>
        (document.body.querySelector(`[aria-label="使用 ${title}"]`) as HTMLButtonElement).click(),
      );
    await pick('Keyed');
    expect(input('Header 名稱').value).toBe('X-Key');
    type(input('API key'), 'secret');
    await click('上一步');
    await pick('Plain');
    expect(input('Header 名稱')).toBeUndefined();
    expect(input('Token').value).toBe('');
  });
});
