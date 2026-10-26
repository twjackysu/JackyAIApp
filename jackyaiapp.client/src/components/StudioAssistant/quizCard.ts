import type { CustomCard } from '@/components/aiChat/cards/toolCard';

/**
 * Studio AI's review quiz (`review_quiz`, a `quiz` card): a few multiple-choice questions on the reader's own
 * saved words, answered on the card. A card comes from a tool, so `parseQuizCard` keeps only plain text and
 * numbers within limits (React escapes the text), and a question that does not add up is dropped.
 */

export interface QuizQuestion {
  /** The reader's saved word the answer is recorded against (the review schedule's id). */
  userWordId: number;
  word: string;
  prompt: string;
  options: string[];
  correctIndex: number;
  explanation: string | null;
  /** Whether the word is due for review, so its answer goes to the schedule (an early one would push it out). */
  due: boolean;
}

export interface QuizCardData {
  title: string;
  questions: QuizQuestion[];
  /** Whether the words are those due for review (else the soonest), and how many are due in all. */
  fromDue: boolean;
  totalDue: number;
}

const MAX_QUESTIONS = 10;
const MAX_OPTIONS = 6;
const MAX_TEXT = 500;

const isRecord = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

const text = (v: unknown, max = MAX_TEXT): string | null =>
  typeof v === 'string' && v.trim() ? v.trim().slice(0, max) : null;

const count = (v: unknown): number | null =>
  typeof v === 'number' && Number.isInteger(v) && v >= 0 ? v : null;

/** A question's `due`, or for a card saved before questions said it, the whole quiz's `fromDue`. */
function parseQuestion(raw: unknown, fromDue: boolean): QuizQuestion | null {
  if (!isRecord(raw)) return null;
  const userWordId = count(raw.userWordId);
  const word = text(raw.word, 60);
  const prompt = text(raw.prompt);
  if (!userWordId || !word || !prompt || !Array.isArray(raw.options)) return null;
  const options = raw.options.map((o) => text(o, 300));
  if (options.length < 2 || options.length > MAX_OPTIONS || options.some((o) => o === null))
    return null;
  const correctIndex = count(raw.correctIndex);
  if (correctIndex === null || correctIndex >= options.length) return null;
  return {
    userWordId,
    word,
    prompt,
    options: options as string[],
    correctIndex,
    explanation: text(raw.explanation),
    due: raw.due === undefined ? fromDue : raw.due === true,
  };
}

/** The quiz a `quiz` card holds, or null when it holds no question that adds up. */
export function parseQuizCard(card: CustomCard): QuizCardData | null {
  const raw = card.data;
  if (!Array.isArray(raw.questions)) return null;
  const fromDue = raw.fromDue === true;
  const questions = raw.questions
    .slice(0, MAX_QUESTIONS)
    .map((q) => parseQuestion(q, fromDue))
    .filter((q): q is QuizQuestion => q !== null);
  if (questions.length === 0) return null;
  return {
    title: card.title ?? '複習小測驗',
    questions,
    fromDue,
    totalDue: count(raw.totalDue) ?? 0,
  };
}

/** SM-2 quality for a multiple-choice answer, as the review page sends it: right 4, wrong 1. */
export const qualityOf = (correct: boolean) => (correct ? 4 : 1);

/** What the card says once every question is answered. */
export function quizSummary(right: number, total: number): string {
  if (right === total) return `全對！${total} 題都答對了`;
  if (right === 0) return `這 ${total} 題都還不熟，複習排程會讓它們早點再出現`;
  return `答對 ${right} / ${total} 題，答錯的字會早點再出現在複習裡`;
}
