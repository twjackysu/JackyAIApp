import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import StarterPacks from './StarterPacks';

const mocks = vi.hoisted(() => ({ add: vi.fn() }));
vi.mock('@/apis/repositoryApis', () => ({
  useGetWordPacksQuery: () => ({
    data: { data: [{ id: 'p1', name: 'Travel', wordCount: 10, description: 'desc' }] },
    isLoading: false,
  }),
  useAddWordPackMutation: () => [mocks.add],
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

async function clickAdd() {
  await act(async () => {
    root.render(
      <MemoryRouter>
        <StarterPacks />
      </MemoryRouter>,
    );
  });
  const add = Array.from(container.querySelectorAll('button')).find((b) =>
    b.textContent?.includes('Add'),
  )!;
  await act(async () => {
    add.click();
  });
}

describe('StarterPacks', () => {
  beforeEach(() => {
    mocks.add.mockReset();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('加入成功：出現前往複習與開始測驗兩個入口', async () => {
    mocks.add.mockReturnValue({ unwrap: () => Promise.resolve({ data: { added: 5 } }) });
    await clickAdd();
    const links = Array.from(container.querySelectorAll('a')).map((a) => [
      a.textContent,
      a.getAttribute('href'),
    ]);
    expect(links).toContainEqual(['前往複習', '/english/review']);
    expect(links).toContainEqual(['開始測驗', '/english/exam']);
  });

  it('加入失敗：顯示錯誤提示', async () => {
    mocks.add.mockReturnValue({
      unwrap: () => Promise.reject({ status: 'FETCH_ERROR', error: 'x' }),
    });
    await clickAdd();
    expect(container.textContent).toContain("Couldn't add the word pack");
    expect(container.querySelectorAll('a').length).toBe(0);
  });
});
