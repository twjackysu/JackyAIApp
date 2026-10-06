import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import RoundResultScreen from './RoundResultScreen';

const mocks = vi.hoisted(() => ({ put: vi.fn() }));
vi.mock('@/apis/repositoryApis', () => ({ usePutRepositoryWordMutation: () => [mocks.put] }));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const resolves = () => ({ unwrap: () => Promise.resolve({}) });
const rejects = (status: number) => ({ unwrap: () => Promise.reject({ status }) });

let container: HTMLDivElement;
let root: Root;

function render(wrongWords: { wordId: string; word: string }[]) {
  act(() => {
    root.render(
      <MemoryRouter>
        <RoundResultScreen correct={8} total={10} wrongWords={wrongWords} onRestart={() => {}} />
      </MemoryRouter>,
    );
  });
}

const button = (text: string) =>
  Array.from(container.querySelectorAll('button')).find((b) => b.textContent?.includes(text));

describe('RoundResultScreen', () => {
  beforeEach(() => {
    mocks.put.mockReset();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('有答錯：列出單字並顯示前往複習與全部加入單字庫', () => {
    render([
      { wordId: '1', word: 'apple' },
      { wordId: '2', word: 'banana' },
    ]);
    expect(container.textContent).toContain('apple');
    expect(container.textContent).toContain('banana');
    expect(button('前往複習')).toBeTruthy();
    expect(button('全部加入單字庫')).toBeTruthy();
  });

  it('全對：不顯示答錯區塊', () => {
    render([]);
    expect(container.textContent).not.toContain('本輪答錯的單字');
    expect(button('前往複習')).toBeUndefined();
    expect(button('全部加入單字庫')).toBeUndefined();
  });

  it('加入成功：每個單字送出一次，按鈕變為已全部加入', async () => {
    mocks.put.mockReturnValue(resolves());
    render([
      { wordId: '1', word: 'apple' },
      { wordId: '2', word: 'banana' },
    ]);
    await act(async () => button('全部加入單字庫')!.click());
    expect(mocks.put).toHaveBeenCalledTimes(2);
    expect(button('已全部加入單字庫')?.disabled).toBe(true);
  });

  it('已在庫（400）視為已加入，不顯示錯誤', async () => {
    mocks.put.mockReturnValue(rejects(400));
    render([{ wordId: '1', word: 'apple' }]);
    await act(async () => button('全部加入單字庫')!.click());
    expect(button('已全部加入單字庫')).toBeTruthy();
    expect(document.body.textContent).not.toContain('加入失敗');
  });

  it('加入失敗：顯示錯誤提示，且仍可重試', async () => {
    mocks.put.mockReturnValue(rejects(500));
    render([{ wordId: '1', word: 'apple' }]);
    await act(async () => button('全部加入單字庫')!.click());
    expect(document.body.textContent).toContain('部分單字加入失敗');
    expect(button('全部加入單字庫')?.disabled).toBe(false);
  });
});
