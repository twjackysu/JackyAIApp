import VolumeUpIcon from '@mui/icons-material/VolumeUp';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import IconButton from '@mui/material/IconButton';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import {
  useGetDictationPromptQuery,
  useGradeDictationMutation,
  useRecordExamResultMutation,
} from '@/apis/examApis';
import { DictationGradeResponse } from '@/apis/examApis/types';
import { CREDIT_COSTS } from '@/constants/creditCosts';
import ExamRoundShell from '@/Exam/components/ExamRoundShell';
import { useExamRound } from '@/Exam/hooks/useExamRound';
import { useOneShotAudio } from '@/Exam/hooks/useOneShotAudio';

/** Sentinel key for the word clip in the card's one-shot audio state — there is only ever
 * one clip per prompt, so a fixed key is enough. */
const AUDIO_KEY = '__dictation__';

/** Dictation practice: hear a word's pronunciation (no text shown), type what you heard, submit
 * for grading, then reveal the correct spelling. The GET prompt deliberately withholds the word
 * text — unlike every other exam card, there is nothing to display before grading by design. */
function DictationTestCard() {
  const [userInput, setUserInput] = useState('');
  const [gradeResult, setGradeResult] = useState<DictationGradeResponse | null>(null);
  const [srsNote, setSrsNote] = useState<string | null>(null);

  const { data, isFetching, refetch, isError, error } = useGetDictationPromptQuery();
  const [gradeDictation, { isLoading: isGrading }] = useGradeDictationMutation();
  const [recordExamResult] = useRecordExamResultMutation();
  const round = useExamRound();
  const { playingKey, play } = useOneShotAudio();

  const prompt = data?.data;

  const handlePlay = () => {
    if (!prompt) return;
    play(AUDIO_KEY, `/api/exam/dictation/audio?wordId=${encodeURIComponent(prompt.wordId)}`);
  };

  const handleSubmit = async () => {
    if (!userInput.trim() || !prompt) return;

    try {
      const result = await gradeDictation({
        wordId: prompt.wordId,
        userInput: userInput.trim(),
      }).unwrap();

      setGradeResult(result.data);

      const correct = result.data.isCorrect;
      round.recordAnswer(correct);

      recordExamResult({
        wordId: prompt.wordId,
        examType: 'Dictation',
        correct,
        score: result.data.score,
      })
        .unwrap()
        .then((r) => {
          if (r.data.scheduleUpdated) {
            setSrsNote(
              correct ? '✓ 已記錄，這個字會延後再複習' : '↻ 已記錄，這個字會盡快回到複習清單',
            );
          }
        })
        .catch(() => {
          /* recording is best-effort; ignore failures */
        });
    } catch (err) {
      console.error('Error grading dictation submission:', err);
    }
  };

  const handleNext = () => {
    setUserInput('');
    setGradeResult(null);
    setSrsNote(null);
    if (round.advance()) {
      refetch();
    }
  };

  const handleRestartRound = () => {
    round.resetRound();
    setUserInput('');
    setGradeResult(null);
    setSrsNote(null);
    refetch();
  };

  return (
    <ExamRoundShell
      round={round}
      isFetching={isFetching}
      isError={isError}
      error={error}
      onRestart={handleRestartRound}
    >
      <Card sx={{ mb: 2 }}>
        <CardContent>
          <Typography variant="h6" component="h2" gutterBottom>
            聽寫練習
          </Typography>

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 2 }}>
            <IconButton
              onClick={handlePlay}
              disabled={!prompt || playingKey === AUDIO_KEY}
              aria-label="播放發音"
              size="large"
            >
              <VolumeUpIcon
                color={playingKey === AUDIO_KEY ? 'primary' : 'inherit'}
                fontSize="large"
              />
            </IconButton>
            <Typography variant="body2" color="text.secondary">
              點擊播放，聽單字發音後在下方輸入拼字（可重複播放）
            </Typography>
          </Box>

          <TextField
            fullWidth
            value={userInput}
            onChange={(e) => setUserInput(e.target.value)}
            placeholder="請輸入你聽到的單字"
            disabled={isGrading || !!gradeResult}
            sx={{ mb: 2 }}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !gradeResult) handleSubmit();
            }}
          />

          {!gradeResult && (
            <Box>
              <Button
                variant="contained"
                onClick={handleSubmit}
                disabled={!userInput.trim() || isGrading}
              >
                {isGrading ? '評分中...' : '送出'}
              </Button>
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{ display: 'block', mt: 0.5 }}
              >
                每次出題消耗 {CREDIT_COSTS.DICTATION_TEST} 點使用額度
              </Typography>
            </Box>
          )}
        </CardContent>
      </Card>

      {gradeResult && (
        <Card sx={{ mb: 2 }}>
          <CardContent>
            <Typography
              variant="h6"
              sx={{ color: gradeResult.isCorrect ? 'success.main' : 'error.main' }}
            >
              {gradeResult.isCorrect ? '答對了！' : '答錯了'}
            </Typography>
            <Typography variant="body1" sx={{ mt: 0.5 }}>
              正確拼字: <strong>{gradeResult.correctText}</strong>
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
              接近程度: {gradeResult.score} 分
            </Typography>
            {srsNote && (
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
                {srsNote}
              </Typography>
            )}
            <Box sx={{ mt: 2 }}>
              <Button variant="contained" onClick={handleNext}>
                換一題
              </Button>
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{ display: 'block', mt: 0.5 }}
              >
                下一題消耗 {CREDIT_COSTS.DICTATION_TEST} 點使用額度
              </Typography>
            </Box>
          </CardContent>
        </Card>
      )}
    </ExamRoundShell>
  );
}

export default DictationTestCard;
