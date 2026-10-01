import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormLabel from '@mui/material/FormLabel';
import Paper from '@mui/material/Paper';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';

import { useGetReadingTestQuery, useRecordExamResultMutation } from '@/apis/examApis';
import { ReadingPassageResponse } from '@/apis/examApis/types';
import ExamRoundShell from '@/Exam/components/ExamRoundShell';
import { useExamRound } from '@/Exam/hooks/useExamRound';

interface QuestionAnswerState {
  selectedOptionId: string;
  correct: boolean;
}

/** Passage-level reading comprehension: a short passage plus 3–4 multiple-choice questions,
 * not tied to the user's saved words (general comprehension practice, shared question bank). */
function ReadingTestCard() {
  const { data, isFetching, refetch, isError, error } = useGetReadingTestQuery();
  const [recordExamResult] = useRecordExamResultMutation();
  const round = useExamRound();

  const passage = data?.data;
  const [answers, setAnswers] = useState<Record<string, QuestionAnswerState>>({});

  // Reset per-question state whenever a new passage arrives.
  useEffect(() => {
    setAnswers({});
  }, [passage?.id]);

  const allAnswered =
    !!passage && passage.questions.length > 0 && passage.questions.every((q) => answers[q.id]);

  const handleSelectOption = (questionId: string, optionId: string) => {
    if (answers[questionId]) return; // already answered, options are locked
    const question = passage?.questions.find((q) => q.id === questionId);
    const option = question?.options.find((o) => o.id === optionId);
    if (!question || !option) return;

    const nextAnswers = {
      ...answers,
      [questionId]: { selectedOptionId: optionId, correct: option.isCorrect },
    };
    setAnswers(nextAnswers);
    round.recordAnswer(option.isCorrect);

    // Record once the passage's last question has just been answered — one record-result
    // call per question (same granularity ListeningResponseTestCard uses), fired together.
    if (passage && passage.questions.every((q) => nextAnswers[q.id])) {
      passage.questions.forEach((q) => {
        recordExamResult({
          examType: 'Reading',
          readingPassageId: passage.id,
          correct: nextAnswers[q.id].correct,
        })
          .unwrap()
          .catch(() => {
            /* recording is best-effort; ignore failures */
          });
      });
    }
  };

  const handleNext = () => {
    setAnswers({});
    if (round.advance()) {
      refetch();
    }
  };

  const handleRestartRound = () => {
    round.resetRound();
    setAnswers({});
    refetch();
  };

  return (
    <ExamRoundShell
      round={round}
      isFetching={isFetching}
      isError={isError}
      error={error}
      onRestart={handleRestartRound}
      maxWidth={700}
    >
      {passage && (
        <ReadingPassage
          passage={passage}
          answers={answers}
          onSelectOption={handleSelectOption}
          allAnswered={allAnswered}
          onNext={handleNext}
        />
      )}
    </ExamRoundShell>
  );
}

interface ReadingPassageProps {
  passage: ReadingPassageResponse;
  answers: Record<string, QuestionAnswerState>;
  onSelectOption: (questionId: string, optionId: string) => void;
  allAnswered: boolean;
  onNext: () => void;
}

function ReadingPassage({
  passage,
  answers,
  onSelectOption,
  allAnswered,
  onNext,
}: ReadingPassageProps) {
  return (
    <>
      <Paper variant="outlined" sx={{ p: 2, mb: 3 }}>
        <Typography variant="h6" gutterBottom>
          {passage.title}
        </Typography>
        <Typography variant="body1" sx={{ whiteSpace: 'pre-line' }}>
          {passage.passageText}
        </Typography>
      </Paper>
      {passage.questions.map((question, qi) => {
        const answer = answers[question.id];
        const submitted = !!answer;

        return (
          <FormControl
            key={question.id}
            component="fieldset"
            disabled={submitted}
            fullWidth
            sx={{ mb: 3 }}
          >
            <FormLabel component="legend">
              {qi + 1}. {question.questionText}
            </FormLabel>
            <RadioGroup
              value={answer?.selectedOptionId ?? null}
              onChange={(e) => onSelectOption(question.id, e.target.value)}
            >
              {question.options.map((option, oi) => {
                const letter = String.fromCharCode(65 + oi);
                const showResult =
                  submitted && (option.isCorrect || option.id === answer?.selectedOptionId);
                return (
                  <Box key={option.id}>
                    <FormControlLabel
                      value={option.id}
                      control={<Radio />}
                      label={`${letter}. ${option.text}`}
                    />
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
        );
      })}
      {allAnswered && (
        <Box sx={{ mt: 2 }}>
          <Button variant="contained" color="primary" onClick={onNext}>
            下一篇
          </Button>
        </Box>
      )}
    </>
  );
}

export default ReadingTestCard;
