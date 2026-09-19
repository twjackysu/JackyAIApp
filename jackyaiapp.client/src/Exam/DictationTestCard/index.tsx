import ReplayIcon from '@mui/icons-material/Replay';
import VolumeUpIcon from '@mui/icons-material/VolumeUp';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import IconButton from '@mui/material/IconButton';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';

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

/** Sentinel key for the dictation clip in the shared audio hook — there's only ever one clip
 * per word, unlike the Listening cards' per-option keys. */
const WORD_KEY = '__word__';

/**
 * Dictation practice: hear a word (its text is deliberately never sent to the client before
 * grading), type what you heard, submit for grading, then see the correct spelling.
 * Word-anchored like Cloze — record-result reuses the existing wordId/correct/score fields.
 */
function DictationTestCard() {
  const { data, isFetching, refetch, isError, error } = useGetDictationPromptQuery();
  const [gradeDictation, { isLoading: isGrading }] = useGradeDictationMutation();
  const [recordExamResult] = useRecordExamResultMutation();
  const round = useExamRound();
  const { playingKey, play, resetPlayed } = useOneShotAudio();

  const [userInput, setUserInput] = useState('');
  const [gradeResult, setGradeResult] = useState<DictationGradeResponse | null>(null);

  const wordId = data?.data.wordId;

  // Reset per-word state whenever a new prompt arrives.
  useEffect(() => {
    setUserInput('');
    setGradeResult(null);
  }, [wordId]);

  const handlePlay = () => {
    if (!wordId) return;
    play(WORD_KEY, `/api/exam/dictation/audio?wordId=${encodeURIComponent(wordId)}`);
  };

  const handleSubmit = async () => {
    if (!userInput.trim() || !wordId) return;

    try {
      const result = await gradeDictation({ wordId, userInput: userInput.trim() }).unwrap();
      setGradeResult(result.data);
      round.recordAnswer(result.data.isCorrect);

      recordExamResult({
        examType: 'Dictation',
        wordId,
        correct: result.data.isCorrect,
        score: result.data.score,
      })
        .unwrap()
        .catch(() => {
          /* recording is best-effort; ignore failures */
        });
    } catch (err) {
      console.error('Error grading dictation submission:', err);
    }
  };

  const handleNext = () => {
    resetPlayed();
    if (round.advance()) {
      refetch();
    }
  };

  const handleRestartRound = () => {
    round.resetRound();
    resetPlayed();
    refetch();
  };

  const submitted = !!gradeResult;

  return (
    <ExamRoundShell
      round={round}
      isFetching={isFetching}
      isError={isError}
      error={error}
      onRestart={handleRestartRound}
    >
      <Card>
        <CardContent>
          <Typography variant="h6" component="h2" gutterBottom>
            聽寫練習
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            聽音檔，寫出你聽到的單字
          </Typography>

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 2 }}>
            <IconButton
              onClick={handlePlay}
              disabled={playingKey === WORD_KEY || !wordId}
              aria-label="播放"
              color="primary"
              size="large"
            >
              <VolumeUpIcon
                fontSize="large"
                color={playingKey === WORD_KEY ? 'primary' : 'inherit'}
              />
            </IconButton>
            <IconButton
              onClick={handlePlay}
              disabled={playingKey === WORD_KEY || !wordId}
              aria-label="重播"
            >
              <ReplayIcon />
            </IconButton>
          </Box>

          <TextField
            fullWidth
            value={userInput}
            onChange={(e) => setUserInput(e.target.value)}
            placeholder="請輸入你聽到的單字..."
            disabled={submitted || isGrading}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !submitted) {
                e.preventDefault();
                handleSubmit();
              }
            }}
            sx={{ mb: 2 }}
          />

          {!submitted && (
            <Box>
              <Button
                variant="contained"
                onClick={handleSubmit}
                disabled={!userInput.trim() || isGrading}
              >
                {isGrading ? '批改中...' : '送出'}
              </Button>
            </Box>
          )}

          {submitted && (
            <Box>
              <Typography
                variant="h6"
                sx={{ color: gradeResult.isCorrect ? 'success.main' : 'error.main' }}
              >
                {gradeResult.isCorrect ? '答對了！' : '答錯了'}
              </Typography>
              <Typography variant="body1" sx={{ mt: 0.5 }}>
                正確答案: <strong>{gradeResult.correctText}</strong>
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                接近程度: {gradeResult.score} 分
              </Typography>
              <Box sx={{ mt: 2 }}>
                <Button variant="contained" color="primary" onClick={handleNext}>
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
            </Box>
          )}
        </CardContent>
      </Card>
    </ExamRoundShell>
  );
}

export default DictationTestCard;
