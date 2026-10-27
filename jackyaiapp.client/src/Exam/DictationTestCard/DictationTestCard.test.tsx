import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import DictationTestCard from '.';

const grade = vi.fn();

vi.mock('@/apis/examApis', () => ({
  useGetDictationPromptQuery: () => ({
    data: { data: { wordId: 'w1' } },
    isFetching: false,
    refetch: vi.fn(),
    isError: false,
    error: undefined,
  }),
  useGradeDictationMutation: () => [grade, { isLoading: false }],
  useRecordExamResultMutation: () => [
    vi.fn(() => ({ unwrap: () => Promise.resolve({ data: { scheduleUpdated: false } }) })),
  ],
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

function type(text: string) {
  const box = container.querySelector('input')!;
  act(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(box, text);
    box.dispatchEvent(new Event('input', { bubbles: true }));
  });
}
const errorAlert = () => container.querySelector('.MuiAlert-colorError')?.textContent ?? null;
async function submit() {
  const button = [...container.querySelectorAll('button')].find((b) => b.textContent === '送出')!;
  await act(async () => button.click());
}

describe('DictationTestCard', () => {
  beforeEach(() => {
    grade.mockReset();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    act(() =>
      root.render(
        <MemoryRouter>
          <DictationTestCard />
        </MemoryRouter>,
      ),
    );
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('says why an answer was not graded, keeps it, and clears the note on the next try', async () => {
    grade.mockReturnValueOnce({
      unwrap: () =>
        Promise.reject({ status: 429, data: { error: { message: '今天的額度用完了。' } } }),
    });
    type('serendipity');
    await submit();

    expect(errorAlert()).toBe('今天的額度用完了。');
    expect(container.querySelector('input')!.value).toBe('serendipity');

    grade.mockReturnValueOnce({ unwrap: () => new Promise(() => {}) });
    await submit();
    expect(errorAlert()).toBeNull();
  });

  it('says it in its own words when the server gives no reason', async () => {
    grade.mockReturnValueOnce({ unwrap: () => Promise.reject(new Error('x')) });
    type('serendipity');
    await submit();

    expect(errorAlert()).toBe('評分失敗，請再按一次送出。');
  });
});
