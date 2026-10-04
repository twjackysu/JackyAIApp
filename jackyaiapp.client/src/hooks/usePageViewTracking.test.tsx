import { act, useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import usePageViewTracking from './usePageViewTracking';

const mocks = vi.hoisted(() => ({ user: vi.fn(), track: vi.fn() }));
vi.mock('@/apis/accountApis', () => ({ useGetUserInfoQuery: mocks.user }));
vi.mock('@/utils/analytics', () => ({ trackPageView: mocks.track }));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let go: (to: string) => void;
function Probe() {
  usePageViewTracking();
  const navigate = useNavigate();
  useEffect(() => {
    go = navigate;
  }, [navigate]);
  return null;
}

async function mount() {
  const root = createRoot(document.createElement('div'));
  await act(async () => {
    root.render(
      <MemoryRouter initialEntries={['/finance?x=1']}>
        <Probe />
      </MemoryRouter>,
    );
  });
}

describe('usePageViewTracking', () => {
  beforeEach(() => vi.clearAllMocks());

  it('tracks the initial and each subsequent route (pathname only), with auth flag', async () => {
    mocks.user.mockReturnValue({ data: { data: { name: 'J' } }, isLoading: false });
    await mount();
    expect(mocks.track).toHaveBeenLastCalledWith('/finance', true);
    await act(async () => go('/portfolio'));
    expect(mocks.track).toHaveBeenLastCalledWith('/portfolio', true);
    expect(mocks.track).toHaveBeenCalledTimes(2);
  });

  it('reports visitors as unauthenticated', async () => {
    mocks.user.mockReturnValue({ data: undefined, isLoading: false });
    await mount();
    expect(mocks.track).toHaveBeenCalledWith('/finance', false);
  });

  it('waits for the auth check before the first page view', async () => {
    mocks.user.mockReturnValue({ data: undefined, isLoading: true });
    await mount();
    expect(mocks.track).not.toHaveBeenCalled();
  });
});
