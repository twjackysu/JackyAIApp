import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import Repository from './index';

const mocks = vi.hoisted(() => ({ words: vi.fn(), refetch: vi.fn() }));
vi.mock('@/apis/repositoryApis', () => ({
  useGetRepositoryWordsQuery: mocks.words,
  useGetWordPacksQuery: () => ({ data: undefined, isLoading: false }),
  useAddWordPackMutation: () => [vi.fn()],
}));
vi.mock('../components/WordCard', () => ({ default: () => null }));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

describe('Repository', () => {
  beforeEach(() => {
    mocks.refetch.mockReset();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('查詢失敗：顯示錯誤與重試，不顯示「單字庫是空的」', () => {
    mocks.words.mockReturnValue({
      data: undefined,
      isFetching: false,
      isError: true,
      error: { status: 'FETCH_ERROR', error: 'x' },
      refetch: mocks.refetch,
    });
    act(() =>
      root.render(
        <MemoryRouter>
          <Repository />
        </MemoryRouter>,
      ),
    );
    expect(container.textContent).toContain('Cannot reach the server');
    expect(container.textContent).not.toContain('contains no vocabulary');
    const retry = Array.from(container.querySelectorAll('button')).find(
      (b) => b.textContent === '重試',
    )!;
    act(() => retry.click());
    expect(mocks.refetch).toHaveBeenCalled();
  });

  it('真的沒有單字：仍顯示空庫提示', () => {
    mocks.words.mockReturnValue({
      data: { data: [] },
      isFetching: false,
      isError: false,
      error: undefined,
      refetch: mocks.refetch,
    });
    act(() =>
      root.render(
        <MemoryRouter>
          <Repository />
        </MemoryRouter>,
      ),
    );
    expect(container.textContent).toContain('contains no vocabulary');
  });
});
