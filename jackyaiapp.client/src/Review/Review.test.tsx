import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import Review from '.';

const mocks = vi.hoisted(() => ({ submit: vi.fn() }));

vi.mock('@/apis/reviewApis', () => ({
  useGetDueReviewsQuery: () => ({
    data: {
      data: {
        dueWords: [
          { userWordId: 1, word: 'apple' },
          { userWordId: 2, word: 'brave' },
        ],
        totalDueCount: 2,
      },
    },
    isLoading: false,
    error: undefined,
  }),
  useSubmitReviewsMutation: () => [mocks.submit, { isLoading: false }],
}));
// The card itself is not what is tested: one button that rates it 4.
vi.mock('./components/Flashcard', () => ({
  default: ({ word, onRate }: { word: { word: string }; onRate: (q: number) => void }) => (
    <button onClick={() => onRate(4)}>{word.word}</button>
  ),
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;
const card = () => container.querySelector('button')!;
const alert = () => container.querySelector('.MuiAlert-colorError')?.textContent ?? null;

describe('Review', () => {
  beforeEach(() => {
    localStorage.clear();
    mocks.submit.mockReset();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    act(() => root.render(<Review />));
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('says the round was not saved, and rating the last card again resends it whole', async () => {
    mocks.submit.mockReturnValue({
      unwrap: () => Promise.reject({ status: 'FETCH_ERROR', error: 'offline' }),
    });
    await act(async () => card().click()); // apple
    await act(async () => card().click()); // brave: the round goes

    expect(alert()).toContain('Cannot reach the server');
    expect(alert()).toContain('再評一次這張卡片');
    expect(card().textContent).toBe('brave');

    mocks.submit.mockReturnValue({
      unwrap: () => Promise.resolve({ data: { wordsReviewed: 2, correctCount: 2, xpEarned: 10 } }),
    });
    await act(async () => card().click());

    expect(mocks.submit).toHaveBeenLastCalledWith({
      reviews: [
        { userWordId: 1, quality: 4 },
        { userWordId: 2, quality: 4 },
      ],
    });
    expect(alert()).toBeNull();
  });

  it('says it in its own words when the server gives no reason', async () => {
    mocks.submit.mockReturnValue({ unwrap: () => Promise.reject(new Error('x')) });
    await act(async () => card().click());
    await act(async () => card().click());

    expect(alert()).toContain('這一輪的評分沒有存成功。');
  });
});
