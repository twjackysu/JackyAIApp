import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import ExamRoundShell from './ExamRoundShell';

// The result screen saves words through the store; what it shows is its own test's.
vi.mock('./RoundResultScreen', () => ({ default: () => <div>result</div> }));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

const round = {
  roundComplete: false,
  answered: 0,
  correct: 0,
  roundSize: 10,
  wrongWords: [],
} as unknown as Parameters<typeof ExamRoundShell>[0]['round'];

describe('ExamRoundShell', () => {
  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('取題失敗：顯示錯誤並可重試', () => {
    const onRetry = vi.fn();
    act(() =>
      root.render(
        <MemoryRouter>
          <ExamRoundShell
            round={round}
            isFetching={false}
            isError
            error={{ status: 'FETCH_ERROR', error: 'x' }}
            onRestart={() => {}}
            onRetry={onRetry}
          >
            <div>question</div>
          </ExamRoundShell>
        </MemoryRouter>,
      ),
    );
    expect(container.textContent).toContain('Cannot reach the server');
    expect(container.textContent).not.toContain('question');
    act(() =>
      Array.from(container.querySelectorAll('button'))
        .find((b) => b.textContent === '重試')!
        .click(),
    );
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('選取題目不會叫出 Studio AI 的選取工具列（會洩漏答案），回合結果可以', () => {
    const render = (r: typeof round) =>
      act(() =>
        root.render(
          <MemoryRouter>
            <ExamRoundShell
              round={r}
              isFetching={false}
              isError={false}
              error={undefined}
              onRestart={() => {}}
            >
              <div id="question">question</div>
            </ExamRoundShell>
          </MemoryRouter>,
        ),
      );
    render(round);
    expect(container.querySelector('#question')!.closest('[data-no-selection-ask]')).not.toBeNull();

    render({ ...round, roundComplete: true, answered: 10, correct: 7 } as typeof round);
    expect(container.textContent).toContain('result');
    expect(container.querySelector('[data-no-selection-ask]')).toBeNull();
  });
});
