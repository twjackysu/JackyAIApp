import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import WritingTestCard from '.';

const evaluate = vi.fn();

vi.mock('@/apis/examApis', () => ({
  useGetWritingPromptQuery: () => ({
    data: { data: { topic: 'a memorable trip', requiredWords: [] } },
    isFetching: false,
    refetch: vi.fn(),
    isError: false,
    error: undefined,
  }),
  useEvaluateWritingMutation: () => [evaluate, { isLoading: false }],
  useRecordExamResultMutation: () => [vi.fn()],
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

function typeAnswer(text: string) {
  const box = container.querySelector('textarea')!;
  act(() => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(box, text);
    box.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

// The prompt's own note is an alert too (severity info).
const errorAlert = () => container.querySelector('.MuiAlert-colorError');

async function submit() {
  const button = [...container.querySelectorAll('button')].find(
    (b) => b.textContent === '提交批改',
  )!;
  await act(async () => {
    button.click();
  });
}

describe('WritingTestCard', () => {
  beforeEach(() => {
    evaluate.mockReset();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    act(() =>
      root.render(
        <MemoryRouter>
          <WritingTestCard />
        </MemoryRouter>,
      ),
    );
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('says why a grading failed, in the server’s words (D-165)', async () => {
    evaluate.mockReturnValue({
      unwrap: () =>
        Promise.reject({
          status: 400,
          data: { error: { message: '內容太長了，請縮短後再送出。' } },
        }),
    });
    typeAnswer('Last summer I went to Tainan.');

    await submit();

    expect(errorAlert()?.textContent).toContain('內容太長了，請縮短後再送出。');
  });

  it('falls back to its own words when the server gives none, and clears them on the next try', async () => {
    evaluate.mockReturnValueOnce({ unwrap: () => Promise.reject({ status: 'FETCH_ERROR' }) });
    typeAnswer('Last summer I went to Tainan.');
    await submit();
    expect(errorAlert()).not.toBeNull();

    evaluate.mockReturnValueOnce({ unwrap: () => new Promise(() => {}) });
    await submit();
    expect(errorAlert()).toBeNull();
  });
});
