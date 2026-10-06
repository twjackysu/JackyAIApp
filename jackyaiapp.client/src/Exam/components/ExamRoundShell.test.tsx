import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import ExamRoundShell from './ExamRoundShell';

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
});
