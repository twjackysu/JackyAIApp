import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import QuizCardView from './QuizCardView';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const mocks = vi.hoisted(() => ({
  submit: vi.fn(),
  sent: {} as Record<string, unknown>,
}));
vi.mock('@/apis/reviewApis', () => ({
  useSubmitReviewsMutation: () => [mocks.submit, mocks.sent],
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  mocks.submit = vi.fn();
  mocks.sent = {};
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

const card = (totalDue = 7, fromDue = true) => ({
  kind: 'quiz',
  title: '複習小測驗',
  data: {
    fromDue,
    totalDue,
    questions: [
      {
        userWordId: 11,
        word: 'serendipity',
        prompt: 'What is the meaning of "serendipity"?',
        options: ['luck in finding', 'a fruit', 'a river', 'a tool'],
        correctIndex: 0,
        explanation: 'serendipity: 意外發現美好事物',
      },
      {
        userWordId: 12,
        word: 'ephemeral',
        prompt: 'The joy was ___.',
        options: ['sturdy', 'ephemeral', 'loud', 'green'],
        correctIndex: 1,
        explanation: null,
      },
    ],
  },
});

function render(
  c: ReturnType<typeof card> | { kind: string; title: null; data: object },
  saved = false,
) {
  act(() =>
    root.render(
      <MemoryRouter>
        <QuizCardView card={c as Parameters<typeof QuizCardView>[0]['card']} saved={saved} />
      </MemoryRouter>,
    ),
  );
}

const buttons = () => [...container.querySelectorAll('button')];
const press = (label: string) =>
  act(() =>
    buttons()
      .find((b) => b.textContent?.includes(label))!
      .click(),
  );

describe('QuizCardView', () => {
  it('asks one question at a time, shows the answer at once, then records every answer', () => {
    render(card());
    expect(container.textContent).toContain('第 1 / 2 題');
    expect(container.textContent).toContain('What is the meaning of "serendipity"?');

    press('a fruit');
    expect(container.textContent).toContain('答案是 A');
    expect(container.textContent).toContain('意外發現美好事物');
    // Answered: the options no longer take a press.
    expect(buttons().filter((b) => b.disabled)).toHaveLength(4);

    press('下一題');
    expect(container.textContent).toContain('第 2 / 2 題');
    press('ephemeral');
    expect(container.textContent).toContain('答對了！');
    expect(mocks.submit).not.toHaveBeenCalled();

    press('看結果');
    expect(container.textContent).toContain('答對 1 / 2 題');
    expect(mocks.submit).toHaveBeenCalledTimes(1);
    expect(mocks.submit).toHaveBeenCalledWith({
      reviews: [
        { userWordId: 11, quality: 1 },
        { userWordId: 12, quality: 4 },
      ],
    });
    // Five due in all besides these two.
    expect(container.textContent).toContain('還有 5 個字，去複習');
  });

  it('keeps SelectionBar off a question until it is answered (asking about it would give the answer away)', () => {
    const marked = () =>
      !!container.querySelector('[data-testid="quiz-card"][data-no-selection-ask]');
    render(card());
    expect(marked()).toBe(true);
    press('a fruit');
    expect(marked()).toBe(false);
    press('下一題');
    expect(marked()).toBe(true);
  });

  it('says what was recorded, and offers to try again when it was not', () => {
    mocks.sent = { isSuccess: true, data: { data: { xpEarned: 6 } } };
    render(card(2));
    press('luck in finding');
    press('下一題');
    press('ephemeral');
    press('看結果');
    expect(container.textContent).toContain('全對');
    expect(container.textContent).toContain('已記進複習排程・+6 XP');
    expect(container.textContent).not.toContain('去複習');

    mocks.sent = { isError: true };
    render(card(2));
    expect(container.textContent).toContain('沒有記進複習排程');
    press('再試一次');
    expect(mocks.submit).toHaveBeenCalledTimes(2);
  });

  const answerAll = () => {
    press('luck in finding');
    press('下一題');
    press('ephemeral');
    press('看結果');
  };

  it('records nothing again for a quiz drawn from a reopened conversation: it is practice', () => {
    render(card(), true);
    expect(container.textContent).toContain('練習・第 1 / 2 題');
    answerAll();
    expect(container.textContent).toContain('全對');
    expect(container.textContent).toContain('之前答過，這次是練習');
    expect(mocks.submit).not.toHaveBeenCalled();
  });

  it('records nothing for words not yet due, which an early answer would push further out', () => {
    render(card(0, false));
    answerAll();
    expect(container.textContent).toContain('還沒到期，這次是練習');
    expect(container.textContent).not.toContain('去複習');
    expect(mocks.submit).not.toHaveBeenCalled();
  });

  it('records only the answers on words that are due (a quiz on words the reader named)', () => {
    mocks.sent = { isSuccess: true, data: { data: { xpEarned: 3 } } };
    const base = card(0, false);
    const mixed = {
      ...base,
      data: {
        ...base.data,
        questions: base.data.questions.map((q, n) => ({ ...q, due: n === 0 })),
      },
    };
    render(mixed);
    expect(container.textContent).not.toContain('練習・');
    answerAll();
    expect(mocks.submit).toHaveBeenCalledWith({ reviews: [{ userWordId: 11, quality: 4 }] });
    expect(container.textContent).toContain('已記進複習排程（到期的 1 個字，其餘是練習）・+3 XP');
  });

  it('draws nothing for a quiz card that holds no question', () => {
    render({ kind: 'quiz', title: null, data: { questions: [] } });
    expect(container.innerHTML).toBe('');
  });
});
