import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import DailyChallenge from '.';

const mocks = vi.hoisted(() => ({ submit: vi.fn() }));

vi.mock('@/apis/dailyChallengeApis', () => ({
  useGetChallengeQuery: () => ({
    data: {
      data: {
        questions: [{ id: 1 }],
        challengeDate: '2026-10-27',
        alreadyCompleted: false,
      },
    },
    isLoading: false,
    error: undefined,
  }),
  useGetStatsQuery: () => ({ data: undefined, isLoading: false }),
  useSubmitChallengeMutation: () => [mocks.submit, { isLoading: false }],
}));
vi.mock('@/apis/repositoryApis', () => ({ usePutRepositoryWordMutation: () => [vi.fn()] }));
vi.mock('@/apis/reviewApis', () => ({ useGetDueCountQuery: () => ({ data: undefined }) }));
// The question itself is not what is tested: one button that picks the first option.
vi.mock('./components/QuestionCard', () => ({
  default: ({ onSelect }: { onSelect: (i: number) => void }) => (
    <button onClick={() => onSelect(0)}>pick</button>
  ),
}));
vi.mock('./components/TodayCard', () => ({ default: () => null }));
vi.mock('./components/StatsBar', () => ({ default: () => null }));
vi.mock('./components/ResultScreen', () => ({ default: () => <div>result</div> }));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;
const button = (text: string) =>
  [...container.querySelectorAll('button')].find((b) => b.textContent?.includes(text))!;
const alert = () => container.querySelector('.MuiAlert-colorError')?.textContent ?? null;

describe('DailyChallenge', () => {
  beforeEach(() => {
    localStorage.clear();
    mocks.submit.mockReset();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    act(() => root.render(<DailyChallenge />));
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('says the answers were not submitted, keeps them, and Submit again sends them', async () => {
    mocks.submit.mockReturnValue({
      unwrap: () =>
        Promise.reject({ status: 500, data: { error: { message: '伺服器暫時無法處理。' } } }),
    });
    act(() => button('pick').click());
    await act(async () => button('Submit').click());

    expect(alert()).toContain('伺服器暫時無法處理。');
    expect(alert()).toContain('答案都還在');

    mocks.submit.mockReturnValue({
      unwrap: () =>
        Promise.resolve({
          data: {
            score: 1,
            totalQuestions: 1,
            xpEarned: 10,
            newStreak: 1,
            alreadyCompleted: false,
          },
        }),
    });
    await act(async () => button('Submit').click());

    expect(mocks.submit).toHaveBeenLastCalledWith({
      answers: [{ questionId: 1, selectedIndex: 0 }],
    });
    expect(container.textContent).toContain('result');
    expect(alert()).toBeNull();
  });

  it('says it in its own words when the server gives no reason', async () => {
    mocks.submit.mockReturnValue({ unwrap: () => Promise.reject(new Error('x')) });
    act(() => button('pick').click());
    await act(async () => button('Submit').click());

    expect(alert()).toContain('答案沒有送出成功。');
  });
});
