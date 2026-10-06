import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormLabel from '@mui/material/FormLabel';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import Typography from '@mui/material/Typography';
import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

import { useGetClozeTestQuery, useRecordExamResultMutation } from '@/apis/examApis';
import ExamRoundShell from '@/Exam/components/ExamRoundShell';
import WordRecap from '@/Exam/components/WordRecap';
import { useExamRound } from '@/Exam/hooks/useExamRound';

function shuffle<T>(items: T[]): T[] {
  const arr = [...items];
  for (let i = arr.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [arr[i], arr[j]] = [arr[j], arr[i]];
  }
  return arr;
}

function renderFilledSentence(question: string, answer: string) {
  const parts = question.split(/_{2,}/);
  return parts.flatMap((part, i) =>
    i < parts.length - 1 ? [part, <strong key={i}>{answer}</strong>] : [part],
  );
}

function ClozeTestCard() {
  const [selectedOption, setSelectedOption] = useState<string | null>(null);
  const { data, isFetching, refetch, isError, error } = useGetClozeTestQuery();
  const [submitted, setSubmitted] = useState(false);
  const [recordExamResult] = useRecordExamResultMutation();
  const [srsNote, setSrsNote] = useState<string | null>(null);
  const round = useExamRound();

  // Options are stored in a fixed DB order; reshuffle on the client each time a
  // new question loads so repeat practice doesn't let users memorize positions.
  const shuffledOptions = useMemo(
    () => shuffle(data?.data.options ?? []),
    // Re-shuffle only when the question itself changes, not on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [data?.data.question],
  );

  const isCorrect = selectedOption === data?.data.answer;

  const handleOptionChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setSelectedOption(event.target.value);
  };

  const handleSubmit = () => {
    if (!selectedOption || !data) return;
    setSubmitted(true);

    const correct = selectedOption === data.data.answer;
    const wordId = data.data.wordId;
    round.recordAnswer(correct, wordId ? { wordId, word: data.data.answer } : undefined);
    if (wordId) {
      recordExamResult({ wordId, examType: 'Cloze', correct })
        .unwrap()
        .then((res) => {
          if (res.data.scheduleUpdated) {
            setSrsNote(
              correct ? '✓ 已記錄，這個字會延後再複習' : '↻ 已記錄，這個字會盡快回到複習清單',
            );
          }
        })
        .catch(() => {
          /* recording is best-effort; ignore failures */
        });
    }
  };

  const handleNext = () => {
    setSelectedOption(null);
    setSubmitted(false);
    setSrsNote(null);
    if (round.advance()) {
      refetch();
    }
  };

  const handleRestartRound = () => {
    round.resetRound();
    setSelectedOption(null);
    setSubmitted(false);
    setSrsNote(null);
    refetch();
  };

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (submitted || !data || round.roundComplete) return;
      const index = Number(e.key) - 1;
      if (index >= 0 && index < shuffledOptions.length) {
        setSelectedOption(shuffledOptions[index]);
      } else if (e.key === 'Enter' && selectedOption) {
        handleSubmit();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [submitted, data, shuffledOptions, selectedOption, round.roundComplete]);

  return (
    <ExamRoundShell
      round={round}
      isFetching={isFetching}
      isError={isError}
      error={error}
      onRestart={handleRestartRound}
    >
      <Typography variant="h6" gutterBottom>
        {data?.data.question}
      </Typography>
      <FormControl component="fieldset" disabled={submitted}>
        <FormLabel component="legend">選擇答案:</FormLabel>
        <RadioGroup value={selectedOption} onChange={handleOptionChange}>
          {shuffledOptions.map((option, i) => (
            <FormControlLabel
              key={option}
              value={option}
              control={<Radio />}
              label={`${i + 1}. ${option}`}
            />
          ))}
        </RadioGroup>
      </FormControl>
      {!submitted && (
        <Box sx={{ marginTop: 2 }}>
          <Button
            variant="contained"
            color="primary"
            onClick={handleSubmit}
            disabled={!selectedOption}
          >
            送出
          </Button>
        </Box>
      )}
      {submitted && data && (
        <Box sx={{ marginTop: 2 }}>
          <Typography variant="h6" sx={{ color: isCorrect ? 'success.main' : 'error.main' }}>
            {isCorrect ? '答對了！' : '答錯了'}
          </Typography>
          {!isCorrect && (
            <Typography variant="body1" sx={{ mt: 0.5 }}>
              正確答案: <strong>{data.data.answer}</strong>
            </Typography>
          )}
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            {renderFilledSentence(data.data.question, data.data.answer)}
          </Typography>
          <Link
            to={`/english/dictionary?word=${encodeURIComponent(data.data.answer)}`}
            style={{ display: 'inline-block', marginTop: 8 }}
          >
            查看單字「{data.data.answer}」→
          </Link>
          <WordRecap word={data.data.answer} />
          {srsNote && (
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
              {srsNote}
            </Typography>
          )}
          <Box sx={{ mt: 2 }}>
            <Button variant="contained" color="primary" onClick={handleNext}>
              下一題
            </Button>
          </Box>
        </Box>
      )}
    </ExamRoundShell>
  );
}

export default ClozeTestCard;
