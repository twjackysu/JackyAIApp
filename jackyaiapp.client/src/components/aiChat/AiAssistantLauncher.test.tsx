import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AiAssistantLauncher } from './AiAssistantLauncher';
import { SELECTION_SETTLE_MS } from './selection/readSelection';
import { handoffFromState, handoffState } from './utils/handoff';

import type { AssistantActivity } from '.';

import { FINANCE_AI_CHAT } from '@/Finance/aiChatConfig';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

interface PanelProps {
  active: boolean;
  inputValue: string;
  onInputChange: (value: string) => void;
  onCollapse: () => void;
  onActivityChange?: (activity: AssistantActivity) => void;
  onNavigate?: (path: string) => void;
  inputQuotesPageText?: boolean;
  ask?: { id: number; text: string; fresh?: boolean; quotesPageText?: boolean };
  onAskSent?: (id: number) => void;
}

// The real panel is tested on its own; here only what the launcher hands it and does with it.
const panel = vi.hoisted(() => ({ mounts: 0, props: null as PanelProps | null }));
vi.mock('.', () => {
  function StubPanel(props: PanelProps) {
    panel.props = props;
    useEffect(() => {
      panel.mounts += 1;
    }, []);
    return <div data-testid="panel">panel</div>;
  }
  return { default: StubPanel };
});

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  panel.mounts = 0;
  panel.props = null;
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.restoreAllMocks();
});

/** Where the router is (a selected word's dictionary page is a page to go to). */
function Location() {
  const { pathname, state } = useLocation();
  return (
    <div data-testid="location" data-handoff={handoffFromState(state) ?? ''}>
      {pathname}
    </div>
  );
}

async function render(
  auth: { isAuthenticated: boolean; isAuthLoading: boolean },
  onNavigate?: (path: string) => void,
  entry: string | { pathname: string; state: unknown } = '/finance',
) {
  await act(async () =>
    root.render(
      <MemoryRouter initialEntries={[entry]}>
        <AiAssistantLauncher config={FINANCE_AI_CHAT} {...auth} onNavigate={onNavigate} />
        <Location />
      </MemoryRouter>,
    ),
  );
}
const signedIn = { isAuthenticated: true, isAuthLoading: false };

const box = () => container.querySelector<HTMLInputElement>('input[aria-label="AI 助手"]');
const panelEl = () => container.querySelector<HTMLElement>('[data-testid="panel"]');
const panelShown = () =>
  !!panelEl() && getComputedStyle(panelEl()!.parentElement!).display !== 'none';

/** Lets the lazily loaded panel resolve. */
async function settle() {
  for (let i = 0; i < 3; i++) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 10));
    });
  }
}

async function focusBox() {
  await act(async () => box()!.focus());
  await settle();
}

function type(text: string) {
  act(() => {
    const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
    setValue.call(box(), text);
    box()!.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

/** jsdom has no layout, so the reader's selection is a stand-in of what the browser gives; then it settles. */
async function select(text: string, node: Node) {
  const range = {
    commonAncestorContainer: node,
    getBoundingClientRect: () => ({
      left: 100,
      top: 200,
      width: 80,
      height: 20,
      right: 180,
      bottom: 220,
    }),
  };
  vi.spyOn(window, 'getSelection').mockReturnValue({
    isCollapsed: false,
    rangeCount: 1,
    anchorNode: node,
    focusNode: node,
    getRangeAt: () => range,
    toString: () => text,
    removeAllRanges: () => {},
  } as unknown as Selection);
  await act(async () => {
    document.dispatchEvent(new Event('selectionchange'));
    await new Promise((resolve) => setTimeout(resolve, SELECTION_SETTLE_MS + 20));
  });
}

/** Text on the page around the assistant (an analysis, a digest). */
function pageText(text: string) {
  const p = document.createElement('p');
  p.textContent = text;
  container.appendChild(p);
  return p.firstChild!;
}

const barButtons = () => [
  ...document.querySelectorAll<HTMLButtonElement>('[data-testid="selection-bar"] button'),
];
async function pressBar(label: string) {
  await act(async () =>
    barButtons()
      .find((b) => b.textContent === label)!
      .click(),
  );
  await settle();
}

describe('AiAssistantLauncher', () => {
  it('is a one-line box until it is used, marked as the assistant rather than a search', async () => {
    await render(signedIn);

    expect(box()).not.toBeNull();
    expect(box()!.placeholder).toContain('問 AI 助手');
    expect(container.textContent).toContain('AI 助手');
    expect(container.querySelector('[data-testid="AutoAwesomeIcon"]')).not.toBeNull();
    expect(panelEl()).toBeNull();
  });

  it('opens the full conversation once the cursor is put in it', async () => {
    await render(signedIn);
    await focusBox();

    expect(panelShown()).toBe(true);
    expect(panel.props!.active).toBe(true);
    expect(box()).toBeNull();
  });

  it('also opens from its open button', async () => {
    await render(signedIn);
    await act(async () => {
      container.querySelector<HTMLButtonElement>('button[aria-label="開啟 AI 助手"]')!.click();
    });
    await settle();

    expect(panelShown()).toBe(true);
  });

  it('hands what was typed in the box to the conversation', async () => {
    await render(signedIn);
    type('2330 今天');
    await focusBox();

    expect(panel.props!.inputValue).toBe('2330 今天');
  });

  it('folds back to one line and keeps the same conversation for next time', async () => {
    await render(signedIn);
    await focusBox();

    await act(async () => panel.props!.onCollapse());
    expect(box()).not.toBeNull();
    expect(panelShown()).toBe(false);
    expect(panel.props!.active).toBe(false);

    await focusBox();
    expect(panelShown()).toBe(true);
    expect(panel.mounts).toBe(1);
  });

  it('while folded, says that a reply is being written', async () => {
    await render(signedIn);
    await focusBox();
    await act(async () => panel.props!.onActivityChange!({ busy: true, hasConversation: true }));
    await act(async () => panel.props!.onCollapse());

    expect(box()!.placeholder).toBe('AI 助手回覆中…');
    expect(container.querySelector('[aria-label="AI 助手回覆中"]')).not.toBeNull();
  });

  it('while folded with a conversation, offers to carry on with it', async () => {
    await render(signedIn);
    await focusBox();
    await act(async () => panel.props!.onActivityChange!({ busy: false, hasConversation: true }));
    await act(async () => panel.props!.onCollapse());

    expect(box()!.placeholder).toBe('繼續剛才的對話…');
  });

  it('asks a signed-out visitor to sign in, and does not open', async () => {
    await render({ isAuthenticated: false, isAuthLoading: false });

    expect(box()!.placeholder).toContain('登入後');
    expect([...container.querySelectorAll('button')].some((b) => b.textContent === '登入')).toBe(
      true,
    );

    await focusBox();
    expect(panelEl()).toBeNull();
  });

  it('waits, without asking anyone to sign in, while it does not know who the visitor is', async () => {
    await render({ isAuthenticated: false, isAuthLoading: true });

    expect(box()!.disabled).toBe(true);
    expect(box()!.placeholder).not.toContain('登入');
    expect([...container.querySelectorAll('button')].some((b) => b.textContent === '登入')).toBe(
      false,
    );
  });

  it('goes where an answer asked only once that answer is done, since leaving ends it', async () => {
    const navigate = vi.fn();
    await render(signedIn, navigate);
    await focusBox();

    act(() => panel.props!.onActivityChange!({ busy: true, hasConversation: true }));
    act(() => panel.props!.onNavigate!('/dashboards/d1'));
    expect(navigate).not.toHaveBeenCalled();

    act(() =>
      panel.props!.onActivityChange!({ busy: false, hasConversation: true, answered: true }),
    );
    expect(navigate).toHaveBeenCalledExactlyOnceWith('/dashboards/d1');

    // The next answer that asks nothing goes nowhere.
    act(() => panel.props!.onActivityChange!({ busy: true, hasConversation: true }));
    act(() =>
      panel.props!.onActivityChange!({ busy: false, hasConversation: true, answered: true }),
    );
    expect(navigate).toHaveBeenCalledTimes(1);
  });

  it('stays when the reader stopped the answer, and forgets where it was going', async () => {
    const navigate = vi.fn();
    await render(signedIn, navigate);
    await focusBox();

    act(() => panel.props!.onActivityChange!({ busy: true, hasConversation: true }));
    act(() => panel.props!.onNavigate!('/dashboards/d1'));
    act(() =>
      panel.props!.onActivityChange!({ busy: false, hasConversation: true, answered: false }),
    );
    act(() => panel.props!.onActivityChange!({ busy: true, hasConversation: true }));
    act(() =>
      panel.props!.onActivityChange!({ busy: false, hasConversation: true, answered: true }),
    );

    expect(navigate).not.toHaveBeenCalled();
  });

  describe('a task Studio AI handed to this page', () => {
    const handed = { pathname: '/finance', state: handoffState('台積電最近外資在買還是賣？') };
    const location = () => container.querySelector<HTMLElement>('[data-testid="location"]')!;

    it('is asked once, in a new conversation, and taken out of the history entry', async () => {
      await render(signedIn, undefined, handed);
      await act(async () => new Promise((r) => setTimeout(r, 0)));

      expect(panelShown()).toBe(true);
      expect(panel.props!.ask).toEqual({
        id: 1,
        text: '台積電最近外資在買還是賣？',
        fresh: true,
        handedOverBy: 'Studio AI',
      });
      expect(location().dataset.handoff).toBe('');
      expect(location().textContent).toBe('/finance');

      act(() => panel.props!.onAskSent!(1));
      expect(panel.props!.ask).toBeUndefined();
    });

    it('waits for the sign-in, and asks nothing for a visitor who is not signed in', async () => {
      await render({ isAuthenticated: false, isAuthLoading: true }, undefined, handed);
      expect(panel.props).toBeNull();

      await render({ isAuthenticated: true, isAuthLoading: false }, undefined, handed);
      await act(async () => new Promise((r) => setTimeout(r, 0)));
      expect(panel.props!.ask?.text).toBe('台積電最近外資在買還是賣？');
    });
  });

  it('gives the panel no way to move the page when the page did not ask for one', async () => {
    await render(signedIn);
    await focusBox();

    expect(panel.props!.onNavigate).toBeUndefined();
  });
  describe('selecting text on the page asks this assistant', () => {
    it('explains a passage of the page in a new conversation, as page text', async () => {
      await render(signedIn);
      await select('外資連續五日賣超', pageText('今天外資連續五日賣超，指數收黑。'));

      expect(
        document.querySelector('[data-testid="selection-bar"]')!.getAttribute('aria-label'),
      ).toBe('用 AI 助手 處理選取的文字');
      expect(barButtons().map((b) => b.textContent)).toEqual(['解釋', '翻成英文', '問 AI']);
      await pressBar('解釋');

      expect(panelShown()).toBe(true);
      expect(panel.props!.ask).toMatchObject({
        text: expect.stringContaining('「外資連續五日賣超」'),
        fresh: true,
        quotesPageText: true,
      });
      await act(async () => panel.props!.onAskSent!(panel.props!.ask!.id));
      expect(panel.props!.ask).toBeUndefined();
    });

    it('explains a word rather than offering to save it (it has no repository tool), or opens its dictionary page', async () => {
      await render(signedIn);
      await select('ETF', pageText('ETF'));
      expect(barButtons().map((b) => b.textContent)).toEqual(['解釋', '查字典', '問 AI']);

      await pressBar('查字典');
      expect(container.querySelector('[data-testid="location"]')!.textContent).toBe(
        '/english/dictionary/etf',
      );
      expect(panelEl()).toBeNull();
    });

    it('puts 問 AI after what was typed, sent as page text until the box is emptied', async () => {
      await render(signedIn);
      type('2330');
      await select('本益比偏高', pageText('本益比偏高'));
      await pressBar('問 AI');

      expect(panel.props!.inputValue).toBe('2330\n關於「本益比偏高」，');
      expect(panel.props!.inputQuotesPageText).toBe(true);
      await act(async () => panel.props!.onInputChange(''));
      expect(panel.props!.inputQuotesPageText).toBe(false);
    });

    it("continues the conversation for a selection in the assistant's own answer", async () => {
      await render(signedIn);
      await focusBox();
      const answer = document.createElement('p');
      answer.textContent = 'The yield curve inverted.';
      container.querySelector('[data-ai-assistant]')!.appendChild(answer);
      await select('The yield curve inverted.', answer.firstChild!);
      await pressBar('翻成中文');

      expect(panel.props!.ask!.text).toContain('「The yield curve inverted.」');
      expect(panel.props!.ask!.fresh).toBeUndefined();
      expect(panel.props!.ask!.quotesPageText).toBeUndefined();
    });

    it('opens a dictionary page only once the answer being written is done (leaving would end it)', async () => {
      await render(signedIn);
      await focusBox();
      await act(async () => panel.props!.onActivityChange!({ busy: true, hasConversation: true }));
      await select('ETF', pageText('ETF'));
      await pressBar('查字典');
      expect(container.querySelector('[data-testid="location"]')!.textContent).toBe('/finance');

      await act(async () =>
        panel.props!.onActivityChange!({ busy: false, hasConversation: true, answered: true }),
      );
      expect(container.querySelector('[data-testid="location"]')!.textContent).toBe(
        '/english/dictionary/etf',
      );
    });

    it('offers nothing to a signed-out visitor', async () => {
      await render({ isAuthenticated: false, isAuthLoading: false });
      await select('外資連續五日賣超', pageText('外資連續五日賣超'));
      expect(document.querySelector('[data-testid="selection-bar"]')).toBeNull();
    });
  });
});
