import { act, useEffect, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation, useNavigate, type NavigateFunction } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { askStudio } from './askStudio';
import { isStudioShortcut } from './studioChat';

import StudioAssistant from '.';

import { SELECTION_SETTLE_MS } from '@/components/aiChat/selection/readSelection';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const mocks = vi.hoisted(() => ({
  user: { data: { data: { id: 'u1' } } } as { data: unknown },
  panel: null as {
    active: boolean;
    variant?: string;
    onCollapse: () => void;
    ask?: { id: number; text: string; fresh?: boolean; quotesPageText?: boolean };
    onAskSent?: () => void;
    onNavigate?: (path: string) => void;
    onToolDone?: (tool: string) => void;
    onActivityChange?: (a: { busy: boolean; hasConversation: boolean }) => void;
    inputValue: string;
    onInputChange: (value: string) => void;
    inputQuotesPageText?: boolean;
    inputAccessory?: ReactNode;
  } | null,
  narrow: false,
  boards: [] as { id: string; name: string }[],
  loadBoards: vi.fn(),
  dispatch: vi.fn(),
}));
vi.mock('@mui/material/useMediaQuery', () => ({ default: () => mocks.narrow }));
vi.mock('@/apis/accountApis', () => ({ useGetUserInfoQuery: () => mocks.user }));
vi.mock('@/redux/hooks', () => ({ useAppDispatch: () => mocks.dispatch }));
vi.mock('@/apis/dashboardApis', () => ({
  useLazyGetDashboardsQuery: () => [mocks.loadBoards, { data: { data: mocks.boards } }],
}));
// The real panel is tested on its own; here only what Studio AI hands it.
vi.mock('@/components/aiChat', () => ({
  default: (props: NonNullable<typeof mocks.panel>) => {
    mocks.panel = props;
    return <div data-testid="panel">{props.inputAccessory}</div>;
  },
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  mocks.user = { data: { data: { id: 'u1' } } };
  mocks.panel = null;
  mocks.narrow = false;
  mocks.boards = [];
  mocks.loadBoards = vi.fn();
  mocks.dispatch = vi.fn();
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

const router: { navigate?: NavigateFunction; pathname?: string } = {};
function CaptureNavigate() {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  useEffect(() => {
    router.navigate = navigate;
    router.pathname = pathname;
  }, [navigate, pathname]);
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

  it('reads again what a write of Studio AI changed, so the page under the drawer shows it', async () => {
    await render('/portfolio');
    await act(async () => button()!.click());
    await settle();

    await act(async () => mocks.panel!.onToolDone!('update_holding'));
    expect(mocks.dispatch).toHaveBeenCalledWith(
      expect.objectContaining({
        type: 'portfolioApis/invalidateTags',
        payload: ['Holdings', 'Summary'],
      }),
    );

    mocks.dispatch.mockClear();
    await act(async () => mocks.panel!.onToolDone!('portfolio_summary'));
    expect(mocks.dispatch).not.toHaveBeenCalled();
  });

  it('goes to the page an answer takes the reader to, the drawer and its conversation still open', async () => {
    await render('/');
    await act(async () => button()!.click());
    await settle();

    await act(async () => mocks.panel!.onNavigate!('/english/review'));
    await settle();

    expect(router.pathname).toBe('/english/review');
    expect(mocks.panel!.active).toBe(true);
  });

  it('on a phone, closes once the answer that took the reader there is done', async () => {
    mocks.narrow = true;
    await render('/');
    await act(async () => button()!.click());
    await settle();

    await act(async () => mocks.panel!.onNavigate!('/english/review'));
    await settle();
    // Still answering: they read what to do there first.
    await act(async () => mocks.panel!.onActivityChange!({ busy: true, hasConversation: true }));
    expect(mocks.panel!.active).toBe(true);

    await act(async () => mocks.panel!.onActivityChange!({ busy: false, hasConversation: true }));
    expect(router.pathname).toBe('/english/review');
    expect(mocks.panel!.active).toBe(false);

    // The next answer does not close it again.
    await act(async () => button()!.click());
    await act(async () => mocks.panel!.onActivityChange!({ busy: false, hasConversation: true }));
    expect(mocks.panel!.active).toBe(true);
  });

  it('goes to a page with an assistant of its own only once the answer is done', async () => {
    await render('/');
    await act(async () => button()!.click());
    await settle();

    await act(async () => mocks.panel!.onNavigate!('/finance'));
    await settle();
    expect(router.pathname).toBe('/');

    await act(async () => mocks.panel!.onActivityChange!({ busy: false, hasConversation: true }));
    await settle();
    expect(router.pathname).toBe('/finance');
  });

  it('on a phone, a drawer the reader closed meanwhile is not closed for them later', async () => {
    mocks.narrow = true;
    await render('/');
    await act(async () => button()!.click());
    await settle();
    await act(async () => mocks.panel!.onNavigate!('/english/review'));
    await act(async () => mocks.panel!.onCollapse());
    await settle();
    await act(async () => button()!.click());
    await act(async () => mocks.panel!.onActivityChange!({ busy: false, hasConversation: true }));
    expect(mocks.panel!.active).toBe(true);
  });

  it('drops a message once the panel has sent it, so it is never sent twice', async () => {
    await render('/');
    await act(async () => askStudio('給我今天的簡報'));
    await settle();
    await act(async () => mocks.panel!.onAskSent!());
    expect(mocks.panel!.ask).toBeUndefined();
  });

  it('offers to go straight to a page named in the box, without the AI, and goes there', async () => {
    await render('/');
    await act(async () => button()!.click());
    await settle();
    const quick = () => [...document.querySelectorAll('[data-testid="quick-actions"] button')];
    expect(quick()).toEqual([]);

    await act(async () => mocks.panel!.onInputChange('帶我去複習'));
    expect(quick().map((b) => b.textContent)).toEqual(['前往「複習」']);

    await act(async () => (quick()[0] as HTMLButtonElement).click());
    await settle();
    expect(router.pathname).toBe('/english/review');
    expect(mocks.panel!.active).toBe(false);
    expect(mocks.panel!.inputValue).toBe('');
  });

  it('drops the page an answer still being written would take the reader to, once they went elsewhere', async () => {
    await render('/');
    await act(async () => button()!.click());
    await settle();
    await act(async () => mocks.panel!.onActivityChange!({ busy: true, hasConversation: true }));
    await act(async () => mocks.panel!.onNavigate!('/finance'));
    await act(async () => mocks.panel!.onInputChange('複習'));
    const quick = document.querySelector<HTMLButtonElement>('[data-testid="quick-actions"] button');
    await act(async () => quick!.click());
    await settle();

    // A later result of the same answer does not move them again either.
    await act(async () => mocks.panel!.onNavigate!('/portfolio'));
    await settle();
    expect(router.pathname).toBe('/english/review');

    await act(async () => mocks.panel!.onActivityChange!({ busy: false, hasConversation: true }));
    await settle();
    expect(router.pathname).toBe('/english/review');

    // The next answer may take them somewhere again.
    await act(async () => mocks.panel!.onNavigate!('/portfolio'));
    await settle();
    expect(router.pathname).toBe('/portfolio');
  });

  it('offers the dictionary page of one English word typed in the box', async () => {
    await render('/english');
    await act(async () => button()!.click());
    await settle();
    await act(async () => mocks.panel!.onInputChange('serendipity'));
    const quick = [...document.querySelectorAll('[data-testid="quick-actions"] button')];
    expect(quick.map((b) => b.textContent)).toEqual(['查字典：serendipity']);
    await act(async () => (quick[0] as HTMLButtonElement).click());
    await settle();
    expect(router.pathname).toBe('/english/dictionary/serendipity');
  });

  it('offers to open one of the reader’s dashboards by its name, read again each time it opens', async () => {
    mocks.boards = [{ id: 'b1', name: '每月業績追蹤' }];
    await render('/');
    expect(mocks.loadBoards).not.toHaveBeenCalled();
    await act(async () => button()!.click());
    await settle();
    expect(mocks.loadBoards).toHaveBeenCalledTimes(1);

    await act(async () => mocks.panel!.onInputChange('業績'));
    const quick = [...document.querySelectorAll('[data-testid="quick-actions"] button')];
    expect(quick.map((b) => b.textContent)).toEqual(['打開「每月業績追蹤」']);
    await act(async () => (quick[0] as HTMLButtonElement).click());
    await settle();
    expect(router.pathname).toBe('/dashboards/b1');

    await act(async () => button()!.click());
    await settle();
    expect(mocks.loadBoards).toHaveBeenCalledTimes(2);
  });

  it('is not opened by a page’s ask where it is not shown', async () => {
    await render('/finance');
    await act(async () => askStudio('給我今天的簡報'));
    await settle();
    expect(mocks.panel).toBeNull();
  });

  describe('what the reader selected on the page', () => {
    const select = (text: string, where: Element = container) => {
      const node = document.createElement('p');
      node.textContent = text;
      where.appendChild(node);
      vi.spyOn(window, 'getSelection').mockReturnValue({
        isCollapsed: false,
        rangeCount: 1,
        anchorNode: node.firstChild,
        focusNode: node.firstChild,
        getRangeAt: () => ({
          commonAncestorContainer: node.firstChild,
          getBoundingClientRect: () => ({
            left: 10,
            top: 300,
            width: 50,
            height: 20,
            right: 60,
            bottom: 320,
          }),
        }),
        toString: () => text,
        removeAllRanges: () => {},
      } as unknown as Selection);
      return act(async () => {
        document.dispatchEvent(new Event('selectionchange'));
        await new Promise((r) => setTimeout(r, SELECTION_SETTLE_MS + 10));
      });
    };
    const press = (label: string) =>
      act(async () =>
        [...document.querySelectorAll<HTMLButtonElement>('[data-testid="selection-bar"] button')]
          .find((b) => b.textContent === label)!
          .click(),
      );
    afterEach(() => vi.restoreAllMocks());

    it('explains it in a conversation of its own', async () => {
      await render('/english/repository');
      await select('It never rains but it pours.');
      await press('解釋');
      await settle();

      expect(mocks.panel).toMatchObject({
        active: true,
        ask: {
          text: '請解釋我在這一頁選取的這段：\n\n「It never rains but it pours.」',
          fresh: true,
          quotesPageText: true,
        },
      });
    });

    it('puts a question about it in the box for the reader to finish', async () => {
      await render('/portfolio');
      await select('殖利率');
      await press('問 AI');
      await settle();

      expect(mocks.panel).toMatchObject({
        active: true,
        inputValue: '關於「殖利率」，',
        inputQuotesPageText: true,
      });
      expect(mocks.panel!.ask).toBeUndefined();

      // Sent (the box emptied): what is typed next is the reader's own.
      await act(async () => mocks.panel!.onInputChange(''));
      expect(mocks.panel!.inputQuotesPageText).toBe(false);
    });

    it('opens one English word’s dictionary page without the AI', async () => {
      await render('/english/repository');
      await select('Resilient');
      await press('查字典');
      await settle();

      expect(router.pathname).toBe('/english/dictionary/resilient');
      expect(mocks.panel).toBeNull();
    });

    it('asks about a word of one of its own answers in the conversation on screen', async () => {
      await render('/english/repository');
      await act(async () => button()!.click());
      await settle();
      await select('ephemeral', document.querySelector('[data-testid="panel"]')!);
      await press('加入單字庫');
      await settle();

      expect(mocks.panel!.ask).toMatchObject({ text: '把「ephemeral」加入我的單字庫' });
      expect(mocks.panel!.ask!.fresh).toBeUndefined();
      expect(mocks.panel!.ask!.quotesPageText).toBeUndefined();
    });

    it('adds a question about the selection after what the reader had typed', async () => {
      await render('/portfolio');
      await act(async () => button()!.click());
      await settle();
      await act(async () => mocks.panel!.onInputChange('幫我看看'));
      await select('殖利率', document.querySelector('[data-testid="panel"]')!);
      await press('問 AI');
      await settle();

      expect(mocks.panel!.inputValue).toBe('幫我看看\n關於「殖利率」，');
      // Studio AI's own answer is in this conversation already, not page text.
      expect(mocks.panel!.inputQuotesPageText).toBe(false);
    });
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
