import { describe, expect, it } from 'vitest';

import { parseQuizCard, qualityOf, quizSummary } from './quizCard';

const question = (patch: Record<string, unknown> = {}) => ({
  userWordId: 11,
  word: 'serendipity',
  prompt: 'What is the meaning of "serendipity"?',
  options: ['luck in finding', 'a fruit', 'a river', 'a tool'],
  correctIndex: 0,
  explanation: 'serendipity: luck in finding',
  ...patch,
});

const card = (data: Record<string, unknown>, title: string | null = '複習小測驗') => ({
  kind: 'quiz',
  title,
  data,
});

describe('parseQuizCard', () => {
  it('keeps a quiz that adds up', () => {
    expect(parseQuizCard(card({ questions: [question()], fromDue: true, totalDue: 7 }))).toEqual({
      title: '複習小測驗',
      questions: [{ ...question(), due: true }],
      fromDue: true,
      totalDue: 7,
    });
  });

  it('takes each question’s own due, and for a card saved before questions said it, the quiz’s', () => {
    const mixed = parseQuizCard(
      card({
        questions: [question({ due: true }), question({ due: false }), question({ due: 'yes' })],
      }),
    );
    expect(mixed?.questions.map((q) => q.due)).toEqual([true, false, false]);
    const old = parseQuizCard(card({ questions: [question()], fromDue: true }));
    expect(old?.questions[0].due).toBe(true);
  });

  it('drops a question that does not add up, and a quiz with none left', () => {
    const bad = [
      question({ userWordId: 0 }),
      question({ userWordId: '11' }),
      question({ word: '' }),
      question({ options: ['only one'] }),
      question({ options: ['a', 'b', 'c', 'd', 'e', 'f', 'g'] }),
      question({ options: ['a', 3, 'c'] }),
      question({ correctIndex: 4 }),
      question({ correctIndex: -1 }),
      question({ correctIndex: 1.5 }),
      'not a question',
    ];
    const parsed = parseQuizCard(card({ questions: [question({ userWordId: 12 }), ...bad] }));
    expect(parsed?.questions.map((q) => q.userWordId)).toEqual([12]);
    expect(parseQuizCard(card({ questions: bad }))).toBeNull();
    expect(parseQuizCard(card({ questions: 'none' }))).toBeNull();
  });

  it('reads what it is not told as nothing due, and names an untitled quiz', () => {
    const parsed = parseQuizCard(card({ questions: [question({ explanation: 5 })] }, null));
    expect(parsed).toMatchObject({ title: '複習小測驗', fromDue: false, totalDue: 0 });
    expect(parsed?.questions[0].explanation).toBeNull();
  });

  it('holds text to its limits', () => {
    const parsed = parseQuizCard(card({ questions: [question({ prompt: 'x'.repeat(2000) })] }));
    expect(parsed?.questions[0].prompt).toHaveLength(500);
  });
});

describe('qualityOf / quizSummary', () => {
  it('rates an answer as the review page does', () => {
    expect(qualityOf(true)).toBe(4);
    expect(qualityOf(false)).toBe(1);
  });

  it('sums up all right, none right, and some', () => {
    expect(quizSummary(3, 3)).toContain('全對');
    expect(quizSummary(0, 3)).toContain('都還不熟');
    expect(quizSummary(2, 3)).toBe('答對 2 / 3 題，答錯的字會早點再出現在複習裡');
  });
});
