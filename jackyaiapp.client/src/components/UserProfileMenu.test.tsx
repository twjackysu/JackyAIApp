import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import UserProfileMenu, { LOGOUT_FAILED_NOTE } from './UserProfileMenu';

vi.mock('@/apis/accountApis', () => ({
  useGetUserInfoQuery: () => ({
    data: { data: { name: 'Jacky', email: 'jacky@example.com' } },
    isLoading: false,
  }),
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

const alertText = () => document.body.querySelector('[role="alert"]')?.textContent ?? null;
const logoutItem = () =>
  [...document.body.querySelectorAll('[role="menuitem"]')].find((item) =>
    item.textContent?.includes('Logout'),
  ) as HTMLElement;

async function pressLogout() {
  act(() => (container.querySelector('button') as HTMLButtonElement).click());
  await act(async () => logoutItem().click());
}

describe('UserProfileMenu', () => {
  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    act(() => root.render(<UserProfileMenu />));
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
  });

  it('keeps the menu open and says so when the server refuses to sign out', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 500 }));
    await pressLogout();
    expect(alertText()).toBe(LOGOUT_FAILED_NOTE);
    expect(logoutItem()).toBeDefined();
  });

  it('says so when the request never got through', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));
    await pressLogout();
    expect(alertText()).toBe(LOGOUT_FAILED_NOTE);
  });

  it('says nothing before a press', () => {
    act(() => (container.querySelector('button') as HTMLButtonElement).click());
    expect(alertText()).toBeNull();
  });
});
