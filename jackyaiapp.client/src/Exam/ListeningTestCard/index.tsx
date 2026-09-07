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
import { useMemo, useState } from 'react';

import { useGetListeningTestQuery, useRecordExamResultMutation } from '@/apis/examApis';
import AILoading from '@/components/AILoading';
import FetchBaseQueryErrorMessage from '@/components/FetchBaseQueryErrorMessage';
import { CREDIT_COSTS } from '@/constants/creditCosts';
import RoundProgress from '@/Exam/components/RoundProgress';
import RoundResultScreen from '@/Exam/components/RoundResultScreen';
import { useExamRound } from '@/Exam/hooks/useExamRound';
import { useOneShotAudio } from '@/Exam/hooks/useOneShotAudio';
import { shuffle } from '@/Exam/utils/shuffle';

function ListeningTestCard() {
  const [selectedOptionId, setSelectedOptionId] = useState<string | null>(null);
  const { data, isFetching, refetch, isError, error } = useGetListeningTestQuery();
  const [submitted, setSubmitted] = useState(false);
  const [recordExamResult] = useRecordExamResultMutation();
  // Real TOEIC Part 1 plays each statement once — replay is only allowed after answering
  // (for review), which is what the hook's played-once tracking is for.
  const { playingKey, playedKeys, play, resetPlayed } = useOneShotAudio();
  const round = useExamRound();

  // The backend always returns options with the correct one first (both AI generation
  // and the seeded bank preserve authoring order) — reshuffle on the client each time a
  // new question loads so the answer position isn't predictable.
  const options = useMemo(
    () => shuffle(data?.data.options ?? []),
    // Re-shuffle only when the question itself changes, not on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [data?.data.id],
  );
  const selectedOption = options.find((o) => o.id === selectedOptionId);
  const correctOption = options.find((o) => o.isCorrect);
  const isCorrect = selectedOption?.isCorrect ?? false;

  const playOption = (optionId: string) => {
    play(optionId, `/api/exam/listening/audio?optionId=${encodeURIComponent(optionId)}`);
  };

  const handleOptionChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setSelectedOptionId(event.target.value);
  };

  const handleSubmit = () => {
    if (!selectedOptionId || !data) return;
    setSubmitted(true);

    const correct = selectedOption?.isCorrect ?? false;
    round.recordAnswer(correct);
    recordExamResult({
      examType: 'Listening',
      listeningTestId: data.data.id,
      correct,
    })
      .unwrap()
      .catch(() => {
        /* recording is best-effort; ignore failures */
      });
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
      {data?.data.imageUrl && (
        <Box
          component="img"
          src={data.data.imageUrl}
          alt="Listening question"
          sx={{ width: '100%', borderRadius: 1, mb: 2 }}
        />
      )}
      <FormControl component="fieldset" disabled={submitted} fullWidth>
        <FormLabel component="legend">聽4句描述，選出最符合圖片的選項:</FormLabel>
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
                    onClick={() => playOption(option.id)}
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
              下一題消耗 {CREDIT_COSTS.LISTENING_TEST} 點使用額度
            </Typography>
          </Box>
        </Box>
      )}
    </Box>
  );
}

export default ListeningTestCard;
