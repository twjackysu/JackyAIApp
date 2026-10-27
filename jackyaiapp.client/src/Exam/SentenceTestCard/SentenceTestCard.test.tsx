import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import SentenceTestCard from '.';

const evaluate = vi.fn();

vi.mock('@/Exam/components/WordRecap', () => ({ default: () => null }));

vi.mock('@/apis/examApis', () => ({
  useGetSentenceTestQuery: () => ({
    data: {
      data: {
        word: 'adventure',
        wordId: 'w1',
        prompt: "Use 'adventure' in a sentence about travel.",
        context: 'Travel',
        difficultyLevel: 3,
        grammarPattern: 'Past tense',
        sampleAnswer: 'My trip was an adventure.',
      },
    },
    isFetching: false,
    refetch: vi.fn(),
    isError: false,
    error: undefined,
  }),
  useEvaluateSentenceMutation: () => [evaluate, { isLoading: false }],
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

const errorAlert = () => container.querySelector('.MuiAlert-colorError');

async function submit() {
  const button = [...container.querySelectorAll('button')].find(
    (b) => b.textContent === '提交答案',
  )!;
  await act(async () => {
    button.click();
  });
}

describe('SentenceTestCard', () => {
  beforeEach(() => {
    evaluate.mockReset();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    act(() =>
      root.render(
        <MemoryRouter>
          <SentenceTestCard />
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
    typeAnswer('My trip to Japan was an adventure.');

    await submit();

    expect(errorAlert()?.textContent).toContain('內容太長了，請縮短後再送出。');
  });

  it('falls back to its own words when the server gives none, and clears them on the next try', async () => {
    evaluate.mockReturnValueOnce({ unwrap: () => Promise.reject({ status: 'FETCH_ERROR' }) });
    typeAnswer('My trip to Japan was an adventure.');
    await submit();
    expect(errorAlert()).not.toBeNull();

    evaluate.mockReturnValueOnce({ unwrap: () => new Promise(() => {}) });
    await submit();
    expect(errorAlert()).toBeNull();
  });
});
