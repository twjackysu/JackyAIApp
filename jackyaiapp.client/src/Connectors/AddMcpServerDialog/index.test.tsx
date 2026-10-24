import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { entry, installed, probe } from '../testData';

import AddMcpServerDialog from './index';

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
}));

vi.mock('@/apis/connectorsApis/connectorsApis', () => ({
  useProbeMcpServerMutation: mutation(mocks.probe),
  useCreateInstallationMutation: mutation(mocks.create),
  useSetToolPermissionMutation: mutation(mocks.setPermission),
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

function render(preset = null as ReturnType<typeof entry> | null) {
  act(() =>
    root.render(
      <AddMcpServerDialog open preset={preset} onClose={onClose} onAuthorize={onAuthorize} />,
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
});
