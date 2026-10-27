import MicIcon from '@mui/icons-material/Mic';
import VisibilityIcon from '@mui/icons-material/Visibility';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import { useTheme } from '@mui/material/styles';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { TranslationQualityGradingAssistantResponse } from '@/apis/dictionaryApis/types';
import {
  useGetTranslationTestQuery,
  useLazyGetTranslationQualityGradingQuery,
  useRecordExamResultMutation,
} from '@/apis/examApis';
import { ActionTooltip, InfoPopover } from '@/components/hints';
import ExamRoundShell from '@/Exam/components/ExamRoundShell';
import WordRecap from '@/Exam/components/WordRecap';
import { useExamRound } from '@/Exam/hooks/useExamRound';
import { START_RECORDING_LABEL, useVoiceRecording } from '@/hooks/useVoiceRecording';

type Phase = 'answering' | 'revealed' | 'graded';

const GRADE_COLOR: Record<string, 'success' | 'info' | 'warning' | 'error'> = {
  A: 'success',
  B: 'success',
  C: 'warning',
  D: 'error',
  E: 'error',
};

function TranslationCard() {
  const theme = useTheme();
  const [input, setInput] = useState<string>('');
  const [phase, setPhase] = useState<Phase>('answering');
  const [grading, setGrading] = useState<TranslationQualityGradingAssistantResponse | null>(null);
  const [showWordHint, setShowWordHint] = useState(false);
  const [srsNote, setSrsNote] = useState<string | null>(null);
  const [gradingError, setGradingError] = useState<string | null>(null);

  const { data, isFetching, refetch, isError, error } = useGetTranslationTestQuery();

  const { isRecording, isTranscribing, mediaRecorder, recordingNote, toggleRecording } =
    useVoiceRecording({
      onTranscriptionComplete: (text) => setInput(text),
    });

  const [getTranslationQualityGrading, { isFetching: isGrading }] =
    useLazyGetTranslationQualityGradingQuery();
  const [recordExamResult] = useRecordExamResultMutation();
  const round = useExamRound();

  const resetForNewQuestion = () => {
    setInput('');
    setPhase('answering');
    setGrading(null);
    setShowWordHint(false);
    setSrsNote(null);
    setGradingError(null);
  };

  const handleSubmit = () => {
    if (!input.trim() || !data?.data.chinese || !data?.data.word) return;
    setGradingError(null);

    getTranslationQualityGrading({
      translation: input,
      examinationQuestion: data.data.chinese,
      unfamiliarWords: data.data.word,
    }).then((res) => {
      if (!res.data) {
        setGradingError('批改失敗，請稍後再試一次。');
        return;
      }
      const result = res.data.data;
      setGrading(result);
      setPhase('graded');

      // Grade A/B count as correct; C/D/E as needs-work.
      const correct = result.grade === 'A' || result.grade === 'B';
      const wordId = data?.data.wordId;
      round.recordAnswer(correct, wordId && data ? { wordId, word: data.data.word } : undefined);
      if (wordId) {
        recordExamResult({ wordId, examType: 'Translation', correct })
          .unwrap()
          .then((r) => {
            if (r.data.scheduleUpdated) {
              setSrsNote(
                correct ? '✓ 已記錄，這個字會延後再複習' : '↻ 已記錄，這個字會盡快回到複習清單',
              );
            }
          })
          .catch(() => {
            /* best-effort */
          });
      }
    });
  };

  const handleRetry = () => {
    // Keep the user's current input so they can edit and resubmit for a new grading.
    setPhase('answering');
    setGrading(null);
    setSrsNote(null);
  };

  const handleReveal = () => {
    // Skipping straight to the answer counts as not getting this one — keeps
    // the round tally honest instead of letting skips vanish for free.
    round.recordAnswer(
      false,
      data?.data.wordId ? { wordId: data.data.wordId, word: data.data.word } : undefined,
    );
    setPhase('revealed');
  };

  const handleNext = () => {
    resetForNewQuestion();
    if (round.advance()) {
      refetch();
    }
  };

  const handleRestartRound = () => {
    round.resetRound();
    resetForNewQuestion();
    refetch();
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      if (input.trim() && !isGrading) {
        handleSubmit();
      }
    }
  };

  return (
    <ExamRoundShell
      round={round}
      isFetching={isFetching}
      isError={isError}
      error={error}
      onRestart={handleRestartRound}
      onRetry={refetch}
    >
      <Typography variant="h6" gutterBottom>
        {data?.data.chinese}
      </Typography>

      {showWordHint ? (
        <Chip label={`提示: ${data?.data.word}`} size="small" sx={{ mb: 1 }} />
      ) : (
        <Chip
          label="顯示目標單字提示"
          size="small"
          variant="outlined"
          icon={<VisibilityIcon fontSize="small" />}
          onClick={() => setShowWordHint(true)}
          sx={{ mb: 1, cursor: 'pointer' }}
        />
      )}

      {phase === 'answering' && (
        <>
          <Stack direction="row" spacing={2} sx={{ alignItems: 'flex-start', mt: 1 }}>
            <TextField
              fullWidth
              multiline
              minRows={2}
              maxRows={6}
              label="輸入對應的英文句子"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={handleKeyDown}
              disabled={isGrading}
              autoFocus
            />
            <ActionTooltip
              title={
                mediaRecorder
                  ? isRecording
                    ? '點擊停止錄音'
                    : START_RECORDING_LABEL
                  : '麥克風無法使用'
              }
              arrow
            >
              <IconButton
                onClick={toggleRecording}
                sx={{
                  color: isRecording
                    ? theme.palette.error.main
                    : isTranscribing
                      ? theme.palette.warning.main
                      : theme.palette.action.active,
                }}
                disabled={!mediaRecorder || isTranscribing || isGrading}
              >
                <MicIcon />
              </IconButton>
            </ActionTooltip>
          </Stack>
          {isTranscribing && (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              正在轉換語音為文字...
            </Typography>
          )}
          {recordingNote && !isTranscribing && (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              {recordingNote}
            </Typography>
          )}
          {gradingError && (
            <Alert severity="error" sx={{ mt: 1 }}>
              {gradingError}
            </Alert>
          )}

          <Box sx={{ mt: 2, display: 'flex', gap: 2, alignItems: 'center' }}>
            <Box>
              <Button
                variant="contained"
                color="primary"
                onClick={handleSubmit}
                disabled={!input.trim() || isGrading}
              >
                {isGrading ? '批改中...' : '送出批改'}
              </Button>
            </Box>
            <Button variant="text" onClick={handleReveal} disabled={isGrading}>
              直接看答案
            </Button>
          </Box>
        </>
      )}

      {phase === 'revealed' && (
        <Box sx={{ mt: 2 }}>
          <Alert severity="info">
            <Typography variant="body2" sx={{ fontWeight: 700 }}>
              參考答案
            </Typography>
            <Typography variant="body1">{data?.data.english}</Typography>
          </Alert>
          {data?.data.word && <WordRecap word={data.data.word} />}
          <Box sx={{ mt: 2 }}>
            <Button variant="contained" color="primary" onClick={handleNext}>
              下一題
            </Button>
          </Box>
        </Box>
      )}

      {phase === 'graded' && grading && (
        <Box sx={{ mt: 2 }}>
          <Typography variant="body2" color="text.secondary">
            你的翻譯
          </Typography>
          <Typography variant="body1" sx={{ mb: 1.5 }}>
            {input}
          </Typography>

          <Typography variant="body2" color="text.secondary">
            參考翻譯
          </Typography>
          <Typography variant="body1" sx={{ mb: 1.5 }}>
            {grading.correctedTranslation}
          </Typography>

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
            <Chip label={`評級 ${grading.grade}`} color={GRADE_COLOR[grading.grade]} />
            {!grading.wordUsed && (
              <Chip
                label={`未使用目標單字「${data?.data.word}」`}
                size="small"
                variant="outlined"
              />
            )}
            <InfoPopover
              ariaLabel="評分等級說明"
              iconSize={20}
              content={
                <ul style={{ margin: 0, paddingInlineStart: 20 }}>
                  <li>A: 完美的翻譯，沒有任何語法、拼字或風格錯誤，並且完整保留了所有含義。</li>
                  <li>B: 高品質翻譯，文法或拼字錯誤很少，句子大多流暢。</li>
                  <li>C: 翻譯一般，有一些文法或拼字問題，但主要意義清晰。</li>
                  <li>D: 翻譯品質低下，有遺漏或重大錯誤，以及影響理解的文法問題。</li>
                  <li>E: 翻譯完全不正確，內容與原文不符或相矛盾，錯誤百出。</li>
                </ul>
              }
            />
          </Box>
          <Typography variant="body2">{grading.reason}</Typography>

          {data?.data.word && <WordRecap word={data.data.word} />}

          {srsNote && (
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
              {srsNote}
            </Typography>
          )}

          <Box sx={{ mt: 2, display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
            <Box>
              <Button variant="contained" color="primary" onClick={handleNext}>
                下一題
              </Button>
            </Box>
            <Button variant="outlined" onClick={handleRetry}>
              修改重新送出
            </Button>
          </Box>
        </Box>
      )}
    </ExamRoundShell>
  );
}

export default TranslationCard;
