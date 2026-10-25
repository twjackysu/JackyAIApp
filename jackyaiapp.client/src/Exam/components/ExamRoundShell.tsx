import Box from '@mui/material/Box';
import { SerializedError } from '@reduxjs/toolkit';
import { FetchBaseQueryError } from '@reduxjs/toolkit/query';
import { ReactNode } from 'react';

import { useExamRound } from '../hooks/useExamRound';

import RoundProgress from './RoundProgress';
import RoundResultScreen from './RoundResultScreen';

import { noSelectionAsk } from '@/components/aiChat/selection/readSelection';
import AILoading from '@/components/AILoading';
import FetchBaseQueryErrorMessage from '@/components/FetchBaseQueryErrorMessage';

interface ExamRoundShellProps {
  round: ReturnType<typeof useExamRound>;
  isFetching: boolean;
  isError: boolean;
  error: FetchBaseQueryError | SerializedError | undefined;
  /** Restarts the round from the result screen. */
  onRestart: () => void;
  /** Re-requests the question after a fetch failure. */
  onRetry?: () => void;
  /** Cloze/Translation/Listening use 600; the wider Sentence card overrides it. */
  maxWidth?: number;
  children: ReactNode;
}

/**
 * The frame every fixed-size exam round renders inside: the round-finished screen, then the
 * loading and error states of the question query, and otherwise the progress bar above the
 * card's own question UI. Each test card used to repeat these four branches verbatim, so a
 * change to the order (or to what "loading" looks like) had to be made in four places and
 * could silently disagree between cards.
 */
function ExamRoundShell({
  round,
  isFetching,
  isError,
  error,
  onRestart,
  onRetry,
  maxWidth = 600,
  children,
}: ExamRoundShellProps) {
  if (round.roundComplete) {
    return (
      <RoundResultScreen
        correct={round.correct}
        total={round.answered}
        wrongWords={round.wrongWords}
        onRestart={onRestart}
      />
    );
  }

  if (isFetching) {
    return <AILoading />;
  }

  if (isError) {
    return <FetchBaseQueryErrorMessage error={error} onRetry={onRetry} />;
  }

  return (
    // While a question is answered, selecting its text offers no AI help (the round's result is fine).
    <Box sx={{ maxWidth, margin: 'auto', padding: 2 }} {...noSelectionAsk()}>
      <RoundProgress answered={round.answered} total={round.roundSize} correct={round.correct} />
      {children}
    </Box>
  );
}

export default ExamRoundShell;
