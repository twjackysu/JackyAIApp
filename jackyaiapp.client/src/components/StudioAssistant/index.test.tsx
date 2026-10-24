import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useNavigate, type NavigateFunction } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { askStudio } from './askStudio';
import { isStudioShortcut } from './studioChat';

import StudioAssistant from '.';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const mocks = vi.hoisted(() => ({
  user: { data: { data: { id: 'u1' } } } as { data: unknown },
  panel: null as {
    active: boolean;
    variant?: string;
    onCollapse: () => void;
    ask?: { id: number; text: string; fresh?: boolean };
    onAskSent?: () => void;
  } | null,
}));
vi.mock('@/apis/accountApis', () => ({ useGetUserInfoQuery: () => mocks.user }));
// The real panel is tested on its own; here only what Studio AI hands it.
vi.mock('@/components/aiChat', () => ({
  default: (props: NonNullable<typeof mocks.panel>) => {
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

  it('opens with the message a page asks it, a new one each time', async () => {
    await render('/');
    await act(async () => askStudio('給我今天的簡報'));
    await settle();
    expect(mocks.panel).toMatchObject({ active: true, ask: { id: 1, text: '給我今天的簡報' } });

    await act(async () => mocks.panel!.onCollapse());
    await act(async () => askStudio('給我今天的簡報'));
    await settle();
    expect(mocks.panel).toMatchObject({ active: true, ask: { id: 2, text: '給我今天的簡報' } });
  });

  it('hands on a message asked for a new conversation as such (the briefing)', async () => {
    await render('/');
    await act(async () => askStudio('給我今天的簡報', { fresh: true }));
    await settle();
    expect(mocks.panel!.ask).toEqual({ id: 1, text: '給我今天的簡報', fresh: true });
  });

  it('drops a message once the panel has sent it, so it is never sent twice', async () => {
    await render('/');
    await act(async () => askStudio('給我今天的簡報'));
    await settle();
    await act(async () => mocks.panel!.onAskSent!());
    expect(mocks.panel!.ask).toBeUndefined();
  });

  it('is not opened by a page’s ask where it is not shown', async () => {
    await render('/finance');
    await act(async () => askStudio('給我今天的簡報'));
    await settle();
    expect(mocks.panel).toBeNull();
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
