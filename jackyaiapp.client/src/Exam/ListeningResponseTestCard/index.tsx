import VolumeUpIcon from '@mui/icons-material/VolumeUp';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormLabel from '@mui/material/FormLabel';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import Typography from '@mui/material/Typography';
import { useEffect, useRef, useState } from 'react';

import { useGetListeningResponseTestQuery, useRecordExamResultMutation } from '@/apis/examApis';
import AILoading from '@/components/AILoading';
import FetchBaseQueryErrorMessage from '@/components/FetchBaseQueryErrorMessage';
import { CREDIT_COSTS } from '@/constants/creditCosts';
import RoundProgress from '@/Exam/components/RoundProgress';
import RoundResultScreen from '@/Exam/components/RoundResultScreen';
import { useExamRound } from '@/Exam/hooks/useExamRound';

const PROMPT_KEY = '__prompt__';

function ListeningResponseTestCard() {
  const [selectedOptionId, setSelectedOptionId] = useState<string | null>(null);
  const { data, isFetching, refetch, isError, error } = useGetListeningResponseTestQuery();
  const [submitted, setSubmitted] = useState(false);
  const [recordExamResult] = useRecordExamResultMutation();
  const [playingKey, setPlayingKey] = useState<string | null>(null);
  // Real TOEIC Part 2 plays the prompt and each response once — replay is only allowed after
  // answering (for review). Tracks which prompt/options have already been played this question.
  const [playedKeys, setPlayedKeys] = useState<Set<string>>(new Set());
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const round = useExamRound();

  const options = data?.data.options ?? [];
  const selectedOption = options.find((o) => o.id === selectedOptionId);
  const correctOption = options.find((o) => o.isCorrect);
  const isCorrect = selectedOption?.isCorrect ?? false;

  const play = (key: string, url: string) => {
    if (audioRef.current) {
      audioRef.current.pause();
      audioRef.current = null;
    }
    setPlayingKey(key);
    setPlayedKeys((prev) => new Set(prev).add(key));
    const audio = new Audio(url);
    audioRef.current = audio;
    audio.onended = () => setPlayingKey(null);
    audio.onerror = () => setPlayingKey(null);
    audio.play().catch(() => setPlayingKey(null));
  };

  const playPrompt = () => {
    if (!data) return;
    play(
      PROMPT_KEY,
      `/api/exam/listening/response/prompt-audio?testId=${encodeURIComponent(data.data.id)}`,
    );
  };

  const playOption = (optionId: string) => {
    play(
      optionId,
      `/api/exam/listening/response/option-audio?optionId=${encodeURIComponent(optionId)}`,
    );
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
      examType: 'ListeningResponse',
      listeningResponseTestId: data.data.id,
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
    setPlayedKeys(new Set());
    if (round.advance()) {
      refetch();
    }
  };

  const handleRestartRound = () => {
    round.resetRound();
    setSelectedOptionId(null);
    setSubmitted(false);
    setPlayedKeys(new Set());
    refetch();
  };

  useEffect(() => {
    return () => {
      audioRef.current?.pause();
    };
  }, []);

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

      <Paper variant="outlined" sx={{ p: 2, mb: 2, display: 'flex', alignItems: 'center', gap: 1 }}>
        <IconButton
          onClick={playPrompt}
          disabled={playingKey === PROMPT_KEY || (!submitted && playedKeys.has(PROMPT_KEY))}
          aria-label="播放題目"
        >
          <VolumeUpIcon
            color={playingKey === PROMPT_KEY ? 'primary' : 'inherit'}
            fontSize="large"
          />
        </IconButton>
        <Typography variant="body1" color={submitted ? 'text.primary' : 'text.secondary'}>
          {submitted ? data?.data.prompt : '點擊播放，聽題目後選出最適合的回應'}
        </Typography>
      </Paper>

      <FormControl component="fieldset" disabled={submitted} fullWidth>
        <FormLabel component="legend">選出最適合回應題目的選項:</FormLabel>
        <RadioGroup value={selectedOptionId} onChange={handleOptionChange}>
          {options.map((option, i) => {
            const showResult = submitted && (option.isCorrect || option.id === selectedOptionId);
            const letter = String.fromCharCode(65 + i); // A, B, C
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
              下一題消耗 {CREDIT_COSTS.LISTENING_RESPONSE_TEST} credits
            </Typography>
          </Box>
        </Box>
      )}
    </Box>
  );
}

export default ListeningResponseTestCard;
