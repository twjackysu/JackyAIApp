import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AiAssistantLauncher } from './AiAssistantLauncher';

import type { AssistantActivity } from '.';

import { FINANCE_AI_CHAT } from '@/Finance/aiChatConfig';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

interface PanelProps {
  active: boolean;
  inputValue: string;
  onInputChange: (value: string) => void;
  onCollapse: () => void;
  onActivityChange?: (activity: AssistantActivity) => void;
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
});

async function render(auth: { isAuthenticated: boolean; isAuthLoading: boolean }) {
  await act(async () => root.render(<AiAssistantLauncher config={FINANCE_AI_CHAT} {...auth} />));
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
});
