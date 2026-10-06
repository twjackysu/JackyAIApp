import { useState } from 'react';

export const ROUND_SIZE = 10;

/** A word the user got wrong this round, kept so the result screen can offer next steps. */
export interface WrongWord {
  wordId: string;
  word: string;
}

/**
 * Tracks progress through a fixed-size round of exam questions (Cloze/Translation/
 * Sentence), replacing the previous infinite single-question loop. The test card
 * calls recordAnswer() when a question is graded and advance() from its "next
 * question" handler; advance() returns false once the round is full, at which
 * point the card should show the round result screen instead of fetching another
 * question.
 */
export function useExamRound() {
  const [answered, setAnswered] = useState(0);
  const [correct, setCorrect] = useState(0);
  const [roundComplete, setRoundComplete] = useState(false);
  const [wrongWords, setWrongWords] = useState<WrongWord[]>([]);

  /** Pass `word` so a wrong answer is listed on the result screen (deduped by wordId). */
  const recordAnswer = (isCorrect: boolean, word?: WrongWord) => {
    setAnswered((a) => a + 1);
    if (isCorrect) {
      setCorrect((c) => c + 1);
    } else if (word?.wordId && word.word) {
      setWrongWords((prev) =>
        prev.some((w) => w.wordId === word.wordId) ? prev : [...prev, word],
      );
    }
  };

  /** Call from the "next question" handler. Returns true if the card should fetch another question. */
  const advance = () => {
    if (answered >= ROUND_SIZE) {
      setRoundComplete(true);
      return false;
    }
    return true;
  };

  const resetRound = () => {
    setAnswered(0);
    setCorrect(0);
    setRoundComplete(false);
    setWrongWords([]);
  };

  return {
    wrongWords,
    answered,
    correct,
    roundComplete,
    roundSize: ROUND_SIZE,
    recordAnswer,
    advance,
    resetRound,
  };
}
