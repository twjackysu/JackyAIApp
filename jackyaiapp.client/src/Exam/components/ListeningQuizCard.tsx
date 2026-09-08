import VolumeUpIcon from '@mui/icons-material/VolumeUp';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormLabel from '@mui/material/FormLabel';
import IconButton from '@mui/material/IconButton';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import Typography from '@mui/material/Typography';
import { SerializedError } from '@reduxjs/toolkit';
import { FetchBaseQueryError } from '@reduxjs/toolkit/query';
import { ReactNode, useMemo, useState } from 'react';

import RoundProgress from './RoundProgress';
import RoundResultScreen from './RoundResultScreen';

import AILoading from '@/components/AILoading';
import FetchBaseQueryErrorMessage from '@/components/FetchBaseQueryErrorMessage';
import { useExamRound } from '@/Exam/hooks/useExamRound';
import { useOneShotAudio } from '@/Exam/hooks/useOneShotAudio';
import { shuffle } from '@/Exam/utils/shuffle';

/** The shape both listening question types share: ListeningTestOption (Part 1) and
 * ListeningResponseOption (Part 2) are structurally identical. */
export interface ListeningQuizOption {
  id: string;
  text: string;
  isCorrect: boolean;
  explanation: string;
}

export interface ListeningQuizQuestion {
  id: string;
  options: ListeningQuizOption[];
}

/** The subset of useOneShotAudio a prompt needs to render its own play button. */
export type PromptAudio = Pick<
  ReturnType<typeof useOneShotAudio>,
  'playingKey' | 'playedKeys' | 'play'
>;

interface ListeningQuizCardProps {
  question: ListeningQuizQuestion | undefined;
  isFetching: boolean;
  isError: boolean;
  error: FetchBaseQueryError | SerializedError | undefined;
  /** Fetches the next question. Called when advancing and when restarting a round. */
  refetch: () => void;
  /** The instruction shown above the options ("聽4句描述，選出…"). */
  legend: string;
  /** Credit cost of the next question, shown under the "下一題" button. */
  creditCost: number;
  audioUrlForOption: (optionId: string) => string;
  /** Called once per question when the answer is submitted, so the card can record the
   * result with its own exam type and id field. */
  onAnswer: (questionId: string, correct: boolean) => void;
  /**
   * Rendered above the options: Part 1's picture, Part 2's play-the-prompt panel.
   *
   * It is a render prop rather than a plain node because a prompt that is itself audio has to
   * share this card's one-shot audio state — one clip playing at a time, one played-once
   * history reset per question — instead of owning a second, competing <audio>.
   */
  renderPrompt?: (audio: PromptAudio, submitted: boolean) => ReactNode;
}

/**
 * The shared TOEIC-style listening card: a fixed-size round of multiple-choice questions whose
 * options are audio clips, each playable once until the question is answered.
 *
 * Part 1 (picture description) and Part 2 (best spoken response) differ only in what sits above
 * the options, which audio endpoint the clips come from, and how a result is recorded — so
 * everything else (round progress, client-side option shuffling, the answer/explanation reveal,
 * the result footer) lives here once.
 */
function ListeningQuizCard({
  question,
  isFetching,
  isError,
  error,
  refetch,
  legend,
  creditCost,
  audioUrlForOption,
  onAnswer,
  renderPrompt,
}: ListeningQuizCardProps) {
  const [selectedOptionId, setSelectedOptionId] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  // Real TOEIC plays each clip once — replay is only allowed after answering (for review),
  // which is what the hook's played-once tracking is for.
  const { playingKey, playedKeys, play, resetPlayed } = useOneShotAudio();
  const round = useExamRound();

  // The backend always returns options with the correct one first (both AI generation
  // and the seeded bank preserve authoring order) — reshuffle on the client each time a
  // new question loads so the answer position isn't predictable.
  const options = useMemo(
    () => shuffle(question?.options ?? []),
    // Re-shuffle only when the question itself changes, not on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [question?.id],
  );
  const selectedOption = options.find((o) => o.id === selectedOptionId);
  const correctOption = options.find((o) => o.isCorrect);
  const isCorrect = selectedOption?.isCorrect ?? false;

  const handleOptionChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setSelectedOptionId(event.target.value);
  };

  const handleSubmit = () => {
    if (!selectedOptionId || !question) return;
    setSubmitted(true);

    const correct = selectedOption?.isCorrect ?? false;
    round.recordAnswer(correct);
    onAnswer(question.id, correct);
  };

  const handleNext = () => {
    setSelectedOptionId(null);
    setSubmitted(false);
    resetPlayed();
    if (round.advance()) {
      refetch();
    }
  };

  const handleRestartRound = () => {
    round.resetRound();
    setSelectedOptionId(null);
    setSubmitted(false);
    resetPlayed();
    refetch();
  };

  if (round.roundComplete) {
    return (
      <RoundResultScreen
        correct={round.correct}
        total={round.answered}
        onRestart={handleRestartRound}
      />
    );
  }

  if (isFetching) {
    return <AILoading />;
  }
  if (isError) {
    return <FetchBaseQueryErrorMessage error={error} />;
  }
  return (
    <Box sx={{ maxWidth: 600, margin: 'auto', padding: 2 }}>
      <RoundProgress answered={round.answered} total={round.roundSize} correct={round.correct} />
      {renderPrompt?.({ playingKey, playedKeys, play }, submitted)}
      <FormControl component="fieldset" disabled={submitted} fullWidth>
        <FormLabel component="legend">{legend}</FormLabel>
        <RadioGroup value={selectedOptionId} onChange={handleOptionChange}>
          {options.map((option, i) => {
            const showResult = submitted && (option.isCorrect || option.id === selectedOptionId);
            const letter = String.fromCharCode(65 + i); // A, B, C, D
            return (
              <Box key={option.id}>
                <Box sx={{ display: 'flex', alignItems: 'center' }}>
                  <FormControlLabel
                    value={option.id}
                    control={<Radio />}
                    label={submitted ? `${letter}. ${option.text}` : letter}
                    sx={{ flex: 1 }}
                  />
                  <IconButton
                    onClick={() => play(option.id, audioUrlForOption(option.id))}
                    disabled={playingKey === option.id || (!submitted && playedKeys.has(option.id))}
                    aria-label="播放"
                    size="small"
                  >
                    <VolumeUpIcon color={playingKey === option.id ? 'primary' : 'inherit'} />
                  </IconButton>
                </Box>
                {showResult && (
                  <Typography
                    variant="body2"
                    sx={{
                      ml: 4,
                      mb: 1,
                      color: option.isCorrect ? 'success.main' : 'error.main',
                    }}
                  >
                    {option.explanation}
                  </Typography>
                )}
              </Box>
            );
          })}
        </RadioGroup>
      </FormControl>
      {!submitted && (
        <Box sx={{ marginTop: 2 }}>
          <Button
            variant="contained"
            color="primary"
            onClick={handleSubmit}
            disabled={!selectedOptionId}
          >
            送出
          </Button>
        </Box>
      )}
      {submitted && (
        <Box sx={{ marginTop: 2 }}>
          <Typography variant="h6" sx={{ color: isCorrect ? 'success.main' : 'error.main' }}>
            {isCorrect ? '答對了！' : '答錯了'}
          </Typography>
          {!isCorrect && correctOption && (
            <Typography variant="body1" sx={{ mt: 0.5 }}>
              正確答案: <strong>{correctOption.text}</strong>
            </Typography>
          )}
          <Box sx={{ mt: 2 }}>
            <Button variant="contained" color="primary" onClick={handleNext}>
              下一題
            </Button>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
              下一題消耗 {creditCost} 點使用額度
            </Typography>
          </Box>
        </Box>
      )}
    </Box>
  );
}

export default ListeningQuizCard;
