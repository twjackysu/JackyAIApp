import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import OAuthConsent from './index';

const mocks = vi.hoisted(() => ({
  user: vi.fn(),
  consent: vi.fn(),
  decide: vi.fn(),
  assign: vi.fn(),
}));

vi.mock('@/apis/accountApis', () => ({ useGetUserInfoQuery: () => mocks.user() }));
vi.mock('@/apis/oauthApis', () => ({
  useGetOAuthConsentQuery: (request: unknown) => mocks.consent(request),
  useDecideOAuthMutation: () => [
    (body: unknown) => ({ unwrap: () => Promise.resolve().then(() => mocks.decide(body)) }),
    { isLoading: false },
  ],
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;
const originalLocation = window.location;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  Object.defineProperty(window, 'location', {
    configurable: true,
    value: { ...originalLocation, assign: mocks.assign },
  });
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
  vi.clearAllMocks();
});

const query =
  '?response_type=code&client_id=c1&redirect_uri=https%3A%2F%2Fclaude.ai%2Fcb&state=s1&scope=mcp%3Aread';

function render(signedIn = true) {
  mocks.user.mockReturnValue({
    data: signedIn ? { data: { id: 'u1' } } : undefined,
    isFetching: false,
  });
  act(() =>
    root.render(
      <MemoryRouter initialEntries={[`/oauth/authorize${query}`]}>
        <OAuthConsent />
      </MemoryRouter>,
    ),
  );
}

const text = () => document.body.textContent ?? '';
const button = (label: string) =>
  [...document.body.querySelectorAll('button')].find((b) => b.textContent?.includes(label))!;
const click = async (el: Element) => act(async () => (el as HTMLElement).click());

describe('OAuthConsent', () => {
  it('asks a signed-out visitor to sign in, coming back with the whole request', async () => {
    render(false);
    expect(text()).toContain('請先登入');
    await click(button('用 Google 登入'));
    expect(mocks.assign).toHaveBeenCalledWith(
      `/api/account/login/Google?ReturnUrl=${encodeURIComponent(`/oauth/authorize${query}`)}`,
    );
    expect(mocks.consent).not.toHaveBeenCalled();
  });

  it('says who asks and where the reader goes back to', () => {
    mocks.consent.mockReturnValue({
      data: { clientName: 'Claude', redirectHost: 'claude.ai', wantsWrites: false },
      isLoading: false,
    });
    render();
    expect(mocks.consent).toHaveBeenCalledWith({
      response_type: 'code',
      client_id: 'c1',
      redirect_uri: 'https://claude.ai/cb',
      state: 's1',
      scope: 'mcp:read',
    });
    expect(text()).toContain('Claude 想用你的身分使用 Jacky Studio 的工具');
    expect(text()).toContain('claude.ai');
  });

  it('sends the answer and follows where the server says to go', async () => {
    mocks.consent.mockReturnValue({
      data: { clientName: 'Claude', redirectHost: 'claude.ai', wantsWrites: true },
      isLoading: false,
    });
    mocks.decide.mockReturnValue({ redirectUrl: 'https://claude.ai/cb?code=abc&state=s1' });
    render();
    expect(text()).toContain('它要求了');
    await click(document.body.querySelector('input[type="checkbox"]')!);
    await click(button('允許'));
    expect(mocks.decide).toHaveBeenCalledWith(
      expect.objectContaining({ approve: true, allowWrites: true }),
    );
    expect(mocks.assign).toHaveBeenCalledWith('https://claude.ai/cb?code=abc&state=s1');
  });

  it('declines too', async () => {
    mocks.consent.mockReturnValue({
      data: { clientName: 'Claude', redirectHost: 'claude.ai', wantsWrites: false },
      isLoading: false,
    });
    mocks.decide.mockReturnValue({ redirectUrl: 'https://claude.ai/cb?error=access_denied' });
    render();
    await click(button('拒絕'));
    expect(mocks.decide).toHaveBeenCalledWith(
      expect.objectContaining({ approve: false, allowWrites: false }),
    );
  });

  it('shows why a request cannot go on, sending the reader nowhere', () => {
    mocks.consent.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: {
        status: 400,
        data: { error: { code: 1040000, message: '這個登入要求的返回網址不在它註冊的網址裡' } },
      },
    });
    render();
    expect(text()).toContain('返回網址不在它註冊的網址裡');
    expect(document.body.querySelector('button')).toBeNull();
  });
});
