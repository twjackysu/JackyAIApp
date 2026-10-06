import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import ConversationSummary from './ConversationSummary';

const mocks = vi.hoisted(() => ({ put: vi.fn(), getWord: vi.fn() }));
vi.mock('@/apis/repositoryApis', () => ({ usePutRepositoryWordMutation: () => [mocks.put] }));
vi.mock('@/apis/dictionaryApis', () => ({ useLazyGetWordQuery: () => [mocks.getWord] }));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const summary = {
  correctionsReview: [],
  betterPhrasings: [],
  suggestedWords: ['apple'],
};

let container: HTMLDivElement;
let root: Root;

const chip = () =>
  Array.from(document.body.querySelectorAll('.MuiChip-root')).find((e) =>
    e.textContent?.includes('apple'),
  ) as HTMLElement;

describe('ConversationSummary suggestedWords', () => {
  beforeEach(() => {
    mocks.put.mockReset();
    mocks.getWord.mockReset();
    mocks.getWord.mockReturnValue({ unwrap: () => Promise.resolve({ data: { id: 'w1' } }) });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    act(() => {
      root.render(
        <ConversationSummary
          open
          summary={summary as never}
          isLoading={false}
          onClose={() => {}}
          onStartNew={() => {}}
        />,
      );
    });
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('點擊單字：加入單字庫並顯示成功提示與已加入狀態', async () => {
    mocks.put.mockReturnValue({ unwrap: () => Promise.resolve({}) });
    await act(async () => chip().click());
    expect(mocks.put).toHaveBeenCalledWith('w1');
    expect(document.body.textContent).toContain('已將「apple」加入單字庫');
    expect(chip().getAttribute('aria-label')).toContain('已加入單字庫');
  });

  it('加入失敗：顯示失敗提示，單字仍可再次點擊', async () => {
    mocks.put.mockReturnValue({ unwrap: () => Promise.reject({ status: 500 }) });
    await act(async () => chip().click());
    expect(document.body.textContent).toContain('「apple」加入失敗');
    expect(chip().getAttribute('aria-label')).toContain('加入單字庫：apple');
  });
});
