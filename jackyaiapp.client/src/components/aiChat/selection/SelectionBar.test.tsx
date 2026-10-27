import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, onTestFinished, vi } from 'vitest';

import { noSelectionAsk, readSelection, SELECTION_SETTLE_MS } from './readSelection';
import SelectionBar from './SelectionBar';
import { selectionHint } from './selectionShortcut';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;
let page: HTMLDivElement;
const removeAllRanges = vi.fn();

/** jsdom has no layout, so the reader's selection is played by a stand-in of what the browser gives. */
function fakeSelection(
  text: string | null,
  node: Node,
  rect = { left: 100, top: 200, width: 80, height: 20 },
) {
  const range = {
    commonAncestorContainer: node,
    getBoundingClientRect: () => ({
      ...rect,
      right: rect.left + rect.width,
      bottom: rect.top + rect.height,
    }),
  };
  vi.spyOn(window, 'getSelection').mockReturnValue({
    isCollapsed: text === null,
    rangeCount: text === null ? 0 : 1,
    anchorNode: node,
    focusNode: node,
    getRangeAt: () => range,
    toString: () => text ?? '',
    removeAllRanges,
  } as unknown as Selection);
}

beforeEach(() => {
  vi.useFakeTimers();
  removeAllRanges.mockClear();
  page = document.createElement('div');
  page.innerHTML = '<p id="text">The market rallied today.</p><textarea id="box"></textarea>';
  document.body.appendChild(page);
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  page.remove();
  vi.restoreAllMocks();
  vi.useRealTimers();
});

const bar = () => document.querySelector('[data-testid="selection-bar"]');
const buttons = () => [
  ...document.querySelectorAll<HTMLButtonElement>('[data-testid="selection-bar"] button'),
];

async function selectAndSettle(text: string | null, node: Node) {
  fakeSelection(text, node);
  await act(async () => {
    document.dispatchEvent(new Event('selectionchange'));
    vi.advanceTimersByTime(SELECTION_SETTLE_MS);
  });
}

async function renderBar(enabled = true, onAction = vi.fn()) {
  await act(async () => root.render(<SelectionBar enabled={enabled} onAction={onAction} />));
  return onAction;
}

describe('SelectionBar', () => {
  it('offers what can be done with the selected page text once it settles', async () => {
    await renderBar();
    fakeSelection('The market rallied today.', document.getElementById('text')!.firstChild!);
    await act(async () => {
      document.dispatchEvent(new Event('selectionchange'));
      vi.advanceTimersByTime(SELECTION_SETTLE_MS - 1);
    });
    expect(bar()).toBeNull();

    await act(async () => vi.advanceTimersByTime(1));
    expect(buttons().map((b) => b.textContent)).toEqual(['解釋', '翻成中文', '問 AI']);
  });

  it('hands the text to the action pressed, then clears the selection', async () => {
    const onAction = await renderBar();
    await selectAndSettle('  The market\n rallied today. ', document.getElementById('text')!);

    await act(async () => buttons()[1].click());

    expect(onAction).toHaveBeenCalledWith('translate', 'The market rallied today.', false);
    expect(removeAllRanges).toHaveBeenCalled();
    expect(bar()).toBeNull();
  });

  it('offers the dictionary for one English word', async () => {
    const onAction = await renderBar();
    await selectAndSettle('Resilient', document.getElementById('text')!);

    await act(async () => buttons()[0].click());

    expect(onAction).toHaveBeenCalledWith('dictionary', 'Resilient', false);
  });

  it('leaves alone a selection in a box the reader types in, and one in a dialog', async () => {
    await renderBar();
    await selectAndSettle('some draft text', document.getElementById('box')!);
    expect(bar()).toBeNull();

    const dialog = document.createElement('div');
    dialog.setAttribute('role', 'dialog');
    dialog.textContent = 'inside a dialog';
    page.appendChild(dialog);
    await selectAndSettle('inside a dialog', dialog.firstChild!);
    expect(bar()).toBeNull();
  });

  it("leaves alone what a page marks with noSelectionAsk (a test's question), and only while marked", async () => {
    await renderBar();
    const question = document.createElement('div');
    Object.entries(noSelectionAsk()).forEach(([name, value]) => question.setAttribute(name, value));
    question.innerHTML = '<p>Which word means <b>happy</b>?</p>';
    page.appendChild(question);
    await selectAndSettle('happy', question.querySelector('b')!.firstChild!);
    expect(bar()).toBeNull();

    expect(noSelectionAsk(false)).toEqual({});
    question.removeAttribute('data-no-selection-ask');
    await selectAndSettle('happy', question.querySelector('b')!.firstChild!);
    expect(buttons().map((b) => b.textContent)).toContain('查字典');
  });

  it('leaves alone a drag that runs across a marked question from the text around it', () => {
    page.innerHTML =
      '<p id="above">Question 1</p><div id="q"><b>happy</b></div><p id="below">Next</p>';
    const question = document.getElementById('q')!;
    Object.entries(noSelectionAsk()).forEach(([name, value]) => question.setAttribute(name, value));
    const range = document.createRange();
    range.setStart(document.getElementById('above')!.firstChild!, 0);
    range.setEnd(document.getElementById('below')!.firstChild!, 4);
    range.getBoundingClientRect = () => new DOMRect(100, 200, 80, 60);
    const across = {
      isCollapsed: false,
      rangeCount: 1,
      anchorNode: range.startContainer,
      focusNode: range.endContainer,
      getRangeAt: () => range,
      toString: () => 'Question 1 happy Next',
    } as unknown as Selection;
    vi.spyOn(window, 'getSelection').mockReturnValue(across);
    expect(readSelection()).toBeNull();

    question.removeAttribute('data-no-selection-ask');
    expect(readSelection()).toMatchObject({ text: 'Question 1 happy Next' });
  });

  it('goes when the selection is cleared, or Escape', async () => {
    await renderBar();
    const text = document.getElementById('text')!;
    await selectAndSettle('The market rallied today.', text);
    expect(bar()).not.toBeNull();
    await selectAndSettle(null, text);
    expect(bar()).toBeNull();

    await selectAndSettle('The market rallied today.', text);
    await act(async () =>
      document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })),
    );
    expect(bar()).toBeNull();
  });

  it('takes Escape for itself, not for the drawer the selection is in', async () => {
    await renderBar();
    const drawerKeys = vi.fn();
    page.addEventListener('keydown', drawerKeys);
    await selectAndSettle('The market rallied today.', document.getElementById('text')!);
    await act(async () =>
      page.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })),
    );
    expect(bar()).toBeNull();
    expect(drawerKeys).not.toHaveBeenCalled();

    // With no bar, Escape is the page's again.
    await act(async () =>
      page.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })),
    );
    expect(drawerKeys).toHaveBeenCalledTimes(1);
  });

  it('moves with the selection when something scrolls, and goes once it is gone', async () => {
    await renderBar();
    const text = document.getElementById('text')!;
    await selectAndSettle('The market rallied today.', text);
    fakeSelection('The market rallied today.', text, {
      left: 100,
      top: 300,
      width: 80,
      height: 20,
    });
    await act(async () => window.dispatchEvent(new Event('scroll')));
    expect(getComputedStyle(bar()!).top).toBe('292px');

    fakeSelection(null, text);
    await act(async () => window.dispatchEvent(new Event('scroll')));
    expect(bar()).toBeNull();
  });

  it('stays while it is being pressed, though a touch browser clears the selection on the tap', async () => {
    const onAction = await renderBar();
    await selectAndSettle('The market rallied today.', document.getElementById('text')!);
    await act(async () => bar()!.dispatchEvent(new Event('pointerdown', { bubbles: true })));
    fakeSelection(null, document.getElementById('text')!);
    await act(async () => {
      document.dispatchEvent(new Event('selectionchange'));
      vi.advanceTimersByTime(SELECTION_SETTLE_MS);
    });
    expect(bar()).not.toBeNull();

    await act(async () => buttons()[0].click());
    expect(onAction).toHaveBeenCalledWith('explain', 'The market rallied today.', false);
  });

  it('shows nothing while turned off (Studio AI open, or not on this page)', async () => {
    await renderBar(false);
    await selectAndSettle('The market rallied today.', document.getElementById('text')!);
    expect(bar()).toBeNull();
  });
});

describe('SelectionBar from the keyboard (D-188)', () => {
  const key = (
    init: KeyboardEventInit,
    on: EventTarget = document.activeElement ?? document.body,
  ) =>
    act(async () => {
      on.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init }));
    });

  it('says, without taking the focus, that Alt+A goes to the bar', async () => {
    await renderBar();
    const status = () => document.querySelector('[role="status"]');
    expect(status()!.textContent).toBe('');

    await selectAndSettle('The market rallied today.', document.getElementById('text')!);
    expect(status()!.textContent).toBe(selectionHint('Studio AI'));
    expect(bar()!.getAttribute('aria-keyshortcuts')).toBe('Alt+A');
    expect(document.activeElement).toBe(document.body);
  });

  it('Alt+A goes into the bar, the arrows move along it, Escape goes back', async () => {
    const onAction = await renderBar();
    const link = document.createElement('a');
    link.href = '#';
    link.textContent = 'where the reader was';
    page.appendChild(link);
    link.focus();
    await selectAndSettle('The market rallied today.', document.getElementById('text')!);

    await key({ key: 'å', code: 'KeyA', altKey: true });
    expect(document.activeElement).toBe(buttons()[0]);

    await key({ key: 'ArrowRight' });
    expect(document.activeElement).toBe(buttons()[1]);
    await key({ key: 'ArrowLeft' });
    await key({ key: 'ArrowLeft' });
    expect(document.activeElement).toBe(buttons()[2]);
    await key({ key: 'Home' });
    expect(document.activeElement).toBe(buttons()[0]);

    // A selection change meanwhile (the focus moving) does not take the bar away.
    await act(async () => {
      document.dispatchEvent(new Event('selectionchange'));
      vi.advanceTimersByTime(SELECTION_SETTLE_MS);
    });
    expect(bar()).not.toBeNull();

    // Pressed again from inside, it keeps where the reader was.
    await key({ key: 'å', code: 'KeyA', altKey: true });
    expect(document.activeElement).toBe(buttons()[0]);

    await key({ key: 'Escape' });
    expect(bar()).toBeNull();
    expect(document.activeElement).toBe(link);
    expect(onAction).not.toHaveBeenCalled();
  });

  it('a button pressed from the keyboard hands the text over as a click does', async () => {
    const onAction = await renderBar();
    await selectAndSettle('The market rallied today.', document.getElementById('text')!);
    await key({ key: 'a', code: 'KeyA', altKey: true });
    await act(async () => (document.activeElement as HTMLButtonElement).click());
    expect(onAction).toHaveBeenCalledWith('explain', 'The market rallied today.', false);
    expect(bar()).toBeNull();
  });

  it('is one Tab stop, and a pick from it gives the focus back', async () => {
    await renderBar();
    const link = document.createElement('a');
    link.href = '#';
    link.textContent = 'where the reader was';
    page.appendChild(link);
    link.focus();
    await selectAndSettle('The market rallied today.', document.getElementById('text')!);
    expect(buttons().map((b) => b.tabIndex)).toEqual([0, -1, -1]);

    await key({ key: 'a', code: 'KeyA', altKey: true });
    await key({ key: 'End' });
    await act(async () => (document.activeElement as HTMLButtonElement).click());
    expect(document.activeElement).toBe(link);
  });

  it('takes no other key, and nothing without a bar', async () => {
    await renderBar();
    await key({ key: 'a', code: 'KeyA', altKey: true });
    expect(document.activeElement).toBe(document.body);

    await selectAndSettle('The market rallied today.', document.getElementById('text')!);
    await key({ key: 'a', code: 'KeyA', ctrlKey: true, altKey: true });
    await key({ key: 'a', code: 'KeyA' });
    expect(document.activeElement).toBe(document.body);
  });
});

describe('readSelection', () => {
  it('puts the bar over the selection with a mouse, under it near the top, within the screen', () => {
    const node = document.getElementById('text')!;
    fakeSelection('The market rallied today.', node, {
      left: 100,
      top: 200,
      width: 80,
      height: 20,
    });
    expect(readSelection()).toMatchObject({
      left: 140,
      top: 192,
      below: false,
      inAssistant: false,
    });

    fakeSelection('The market rallied today.', node, { left: 0, top: 20, width: 10, height: 20 });
    expect(readSelection()).toMatchObject({ left: 138, top: 48, below: true });
  });

  it("tells Studio AI's own answers from the page, and takes neither a drawer nor a mix of both", () => {
    const studio = document.createElement('div');
    studio.setAttribute('data-ai-assistant', '');
    studio.className = 'MuiDrawer-root';
    studio.innerHTML = '<p>It might be <b>ephemeral</b>.</p><textarea></textarea>';
    page.appendChild(studio);
    fakeSelection('ephemeral', studio.querySelector('b')!.firstChild!);
    expect(readSelection()).toMatchObject({ text: 'ephemeral', inAssistant: true });

    fakeSelection('typed', studio.querySelector('textarea')!);
    expect(readSelection()).toBeNull();

    const menu = document.createElement('div');
    menu.className = 'MuiDrawer-root';
    menu.textContent = 'Navigation';
    page.appendChild(menu);
    fakeSelection('Navigation', menu.firstChild!);
    expect(readSelection()).toBeNull();
  });

  it('stays on the screen for a selection taller than it, or at its bottom edge on a touch screen', () => {
    const node = document.getElementById('text')!;
    fakeSelection('The market rallied today.', node, {
      left: 100,
      top: -500,
      width: 80,
      height: 2000,
    });
    expect(readSelection()!.top).toBe(window.innerHeight - 52);

    // A touch screen (jsdom has no matchMedia of its own).
    window.matchMedia = (() => ({ matches: true })) as unknown as typeof window.matchMedia;
    onTestFinished(() => {
      delete (window as { matchMedia?: unknown }).matchMedia;
    });
    fakeSelection('The market rallied today.', node, {
      left: 100,
      top: window.innerHeight - 30,
      width: 80,
      height: 20,
    });
    expect(readSelection()).toMatchObject({ below: true, top: window.innerHeight - 52 });
  });

  it('is nothing for one character or an empty box', () => {
    const node = document.getElementById('text')!;
    fakeSelection('a', node);
    expect(readSelection()).toBeNull();
    fakeSelection('The market', node, { left: 0, top: 0, width: 0, height: 0 });
    expect(readSelection()).toBeNull();
  });
});
