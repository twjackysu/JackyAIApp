import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import MemorySection from './index';

import type { AssistantMemory, AssistantMemoryList } from '@/apis/assistantMemoryApis/types';

const mocks = vi.hoisted(() => ({ memories: vi.fn(), forget: vi.fn() }));

vi.mock('@/apis/assistantMemoryApis', () => ({
  useGetAssistantMemoriesQuery: () => mocks.memories(),
  useForgetAssistantMemoryMutation: () => [
    (id: string) => ({ unwrap: () => Promise.resolve().then(() => mocks.forget(id)) }),
    { isLoading: false },
  ],
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;
const notify = vi.fn();

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.clearAllMocks();
});

const memory = (id: string, text: string): AssistantMemory => ({
  id,
  text,
  createdAt: '2026-10-25T12:00:00',
});

function render(list: AssistantMemoryList | undefined, error?: unknown) {
  mocks.memories.mockReturnValue({ data: list && { data: list }, isLoading: false, error });
  act(() => root.render(<MemorySection notify={notify} />));
}

const text = () => container.textContent ?? '';
const button = (label: string) =>
  [...container.querySelectorAll('button')].find((b) => b.textContent === label)!;
const click = async (el: Element) => act(async () => (el as HTMLElement).click());

describe('MemorySection', () => {
  it('says Studio AI remembers nothing yet, and how to have it remember', () => {
    render({ memories: [], limit: 30 });
    expect(text()).toContain('Studio AI 記得的事');
    expect(text()).toContain('還沒有記得任何事');
    expect(text()).toContain('記住我在準備多益');
    expect(text()).toContain('0 / 30');
  });

  it('lists each note, newest first as the server sends them', () => {
    render({
      memories: [memory('m2', '短文用商務情境'), memory('m1', '在準備多益，目標 800 分')],
      limit: 30,
    });
    const notes = [...container.querySelectorAll('[data-testid="memory"]')].map(
      (el) => el.querySelector('p')?.textContent,
    );
    expect(notes).toEqual(['短文用商務情境', '在準備多益，目標 800 分']);
    expect(text()).toContain('2 / 30');
  });

  it('forgets a note only on the second press, and says so', async () => {
    mocks.forget.mockReturnValue(null);
    render({ memories: [memory('m1', '在準備多益')], limit: 30 });

    await click(button('刪除'));
    expect(mocks.forget).not.toHaveBeenCalled();
    await click(button('確定刪除'));

    expect(mocks.forget).toHaveBeenCalledWith('m1');
    expect(notify).toHaveBeenCalledWith('Studio AI 已忘掉「在準備多益」', 'success');
    expect(button('刪除')).toBeTruthy();
  });

  it('can be talked out of forgetting', async () => {
    render({ memories: [memory('m1', '在準備多益')], limit: 30 });

    await click(button('刪除'));
    await click(button('取消'));

    expect(mocks.forget).not.toHaveBeenCalled();
    expect(button('刪除')).toBeTruthy();
  });

  it('keeps the page working when the list cannot be read', () => {
    render(undefined, { status: 500, data: { error: { code: 1500000, message: '伺服器錯誤' } } });
    expect(text()).toContain('記憶目前無法載入');
  });
});
