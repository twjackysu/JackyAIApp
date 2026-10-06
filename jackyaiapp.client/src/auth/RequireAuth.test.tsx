import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import RequireAuth from './RequireAuth';

const mocks = vi.hoisted(() => ({ user: vi.fn() }));
vi.mock('@/apis/accountApis', () => ({ useGetUserInfoQuery: mocks.user }));

function render(fallback?: React.ReactNode) {
  return renderToStaticMarkup(
    <MemoryRouter>
      <RequireAuth fallback={fallback}>
        <div>secret page</div>
      </RequireAuth>
    </MemoryRouter>,
  );
}

const gate = <div>login gate</div>;

describe('RequireAuth fallback', () => {
  beforeEach(() => mocks.user.mockReset());

  it('signed in: renders children, not the fallback', () => {
    mocks.user.mockReturnValue({
      data: { data: { name: 'J' } },
      isFetching: false,
      isError: false,
    });
    const html = render(gate);
    expect(html).toContain('secret page');
    expect(html).not.toContain('login gate');
  });

  it('checking: renders neither, so signed-in users never see the guest screen', () => {
    mocks.user.mockReturnValue({ data: undefined, isFetching: true, isError: false });
    expect(render(gate)).toBe('');
  });

  it('guest: renders the fallback', () => {
    mocks.user.mockReturnValue({ data: undefined, isFetching: false, isError: true });
    const html = render(gate);
    expect(html).toContain('login gate');
    expect(html).not.toContain('secret page');
  });

  it('guest without fallback: renders nothing (redirect path unchanged)', () => {
    mocks.user.mockReturnValue({ data: undefined, isFetching: false, isError: true });
    expect(render()).toBe('');
  });
});
