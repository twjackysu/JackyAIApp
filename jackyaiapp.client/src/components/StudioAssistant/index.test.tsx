import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useNavigate, type NavigateFunction } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { isStudioShortcut } from './studioChat';

import StudioAssistant from '.';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const mocks = vi.hoisted(() => ({
  user: { data: { data: { id: 'u1' } } } as { data: unknown },
  panel: null as { active: boolean; variant?: string; onCollapse: () => void } | null,
}));
vi.mock('@/apis/accountApis', () => ({ useGetUserInfoQuery: () => mocks.user }));
// The real panel is tested on its own; here only what Studio AI hands it.
vi.mock('@/components/aiChat', () => ({
  default: (props: { active: boolean; variant?: string; onCollapse: () => void }) => {
    mocks.panel = props;
    return <div data-testid="panel" />;
  },
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  mocks.user = { data: { data: { id: 'u1' } } };
  mocks.panel = null;
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

async function render(path: string) {
  await act(async () =>
    root.render(
      <MemoryRouter initialEntries={[path]}>
        <CaptureNavigate />
        <StudioAssistant />
      </MemoryRouter>,
    ),
  );
}

const router: { navigate?: NavigateFunction } = {};
function CaptureNavigate() {
  const navigate = useNavigate();
  useEffect(() => {
    router.navigate = navigate;
  }, [navigate]);
  return null;
}

const button = () =>
  document.querySelector<HTMLButtonElement>('button[aria-label="開啟 Studio AI"]');
const settle = () => act(async () => new Promise((r) => setTimeout(r, 0)));

describe('StudioAssistant', () => {
  it('offers its button to a signed-in reader', async () => {
    await render('/english');
    expect(button()).not.toBeNull();
  });

  it('is not there for a visitor who is not signed in', async () => {
    mocks.user = { data: undefined };
    await render('/english');
    expect(button()).toBeNull();
  });

  it('stays out of the way where the page has its own assistant', async () => {
    await render('/finance');
    expect(button()).toBeNull();
  });

  it('opens the assistant as a sheet, and closes it', async () => {
    await render('/');
    await act(async () => button()!.click());
    await settle();

    expect(mocks.panel).toMatchObject({ active: true, variant: 'sheet' });
    await act(async () => mocks.panel!.onCollapse());
    expect(mocks.panel!.active).toBe(false);
  });

  it('keeps the conversation when the reader goes to a page with an assistant of its own', async () => {
    await render('/english');
    await act(async () => button()!.click());
    await settle();
    const panel = document.querySelector('[data-testid="panel"]');

    await act(async () => router.navigate!('/finance'));
    await settle();

    expect(button()).toBeNull();
    expect(mocks.panel!.active).toBe(false);
    expect(document.querySelector('[data-testid="panel"]')).toBe(panel);
  });

  it('opens on ⌘K / Ctrl+K', async () => {
    await render('/');
    await act(async () =>
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true })),
    );
    await settle();

    expect(mocks.panel?.active).toBe(true);
  });
});

describe('isStudioShortcut', () => {
  it('is ⌘K or Ctrl+K, nothing else', () => {
    expect(isStudioShortcut({ key: 'k', metaKey: true, ctrlKey: false, altKey: false })).toBe(true);
    expect(isStudioShortcut({ key: 'K', metaKey: false, ctrlKey: true, altKey: false })).toBe(true);
    expect(isStudioShortcut({ key: 'k', metaKey: false, ctrlKey: false, altKey: false })).toBe(
      false,
    );
    expect(isStudioShortcut({ key: 'k', metaKey: false, ctrlKey: true, altKey: true })).toBe(false);
  });
});
