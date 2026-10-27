import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import {
  useGetWritingPromptQuery,
  useEvaluateWritingMutation,
  useRecordExamResultMutation,
} from '@/apis/examApis';
import { WritingGradingResponse } from '@/apis/examApis/types';
import { getCaughtErrorMessage } from '@/apis/utils/errorMessage';
import ExamRoundShell from '@/Exam/components/ExamRoundShell';
import { useExamRound } from '@/Exam/hooks/useExamRound';

function getScoreColor(score: number) {
  if (score >= 90) return 'success';
  if (score >= 80) return 'info';
  if (score >= 70) return 'warning';
  return 'error';
}

function getScoreText(score: number) {
  if (score >= 90) return '優秀';
  if (score >= 80) return '良好';
  if (score >= 70) return '尚可';
  if (score >= 60) return '及格';
  return '需要改進';
}

/** Open-ended, paragraph-level writing practice: a topic plus a handful of the user's own saved
 * words (or none, for an open-topic prompt), free-text submission, AI feedback. Structurally
 * different from Sentence/Translation's generate-then-grade flow — the prompt fetch is free and
 * only evaluateWriting is usage-gated. */
function WritingTestCard() {
  const [submission, setSubmission] = useState('');
  const [evaluationResult, setEvaluationResult] = useState<WritingGradingResponse | null>(null);
  const [srsNote, setSrsNote] = useState<string | null>(null);
  // The server's reason when it gives one (an answer too long, D-165), else the same words as the translation test.
  const [gradingError, setGradingError] = useState<string | null>(null);

  const { data, isFetching, refetch, isError, error } = useGetWritingPromptQuery();
  const [evaluateWriting, { isLoading: isEvaluating }] = useEvaluateWritingMutation();
  const [recordExamResult] = useRecordExamResultMutation();
  const round = useExamRound();

  const prompt = data?.data;

  const handleSubmit = async () => {
    if (!submission.trim() || !prompt) return;
    setGradingError(null);

    try {
      const result = await evaluateWriting({
        topic: prompt.topic,
        requiredWords: prompt.requiredWords.map((w) => w.wordText),
        submission: submission.trim(),
      }).unwrap();

      setEvaluationResult(result.data);

      // Same ≥60-is-correct threshold SentenceTestCard uses for its own 0–100 score.
      const score = result.data.score;
      const correct = score >= 60;
      round.recordAnswer(correct);

      const writingWordIds = prompt.requiredWords.map((w) => w.wordId);
      recordExamResult({
        examType: 'Writing',
        writingWordIds,
        correct,
        score,
      })
        .unwrap()
        .then((r) => {
          if (r.data.scheduleUpdated) {
            setSrsNote(
              correct ? '✓ 已記錄，這些字會延後再複習' : '↻ 已記錄，這些字會盡快回到複習清單',
            );
          }
        })
        .catch(() => {
          /* best-effort */
        });
    } catch (err) {
      setGradingError(getCaughtErrorMessage(err, '批改失敗，請稍後再試一次。'));
    }
  };

  const handleNext = () => {
    setSubmission('');
    setEvaluationResult(null);
    setSrsNote(null);
    setGradingError(null);
    if (round.advance()) {
      refetch();
    }
  };

  const handleRestartRound = () => {
    round.resetRound();
    setSubmission('');
    setEvaluationResult(null);
    setSrsNote(null);
    setGradingError(null);
    refetch();
  };

  return (
    <ExamRoundShell
      round={round}
      isFetching={isFetching}
      isError={isError}
      error={error}
      onRestart={handleRestartRound}
      onRetry={refetch}
      maxWidth={800}
    >
      <Card sx={{ mb: 2 }}>
        <CardContent>
          <Typography variant="h6" component="h2" gutterBottom>
            寫作練習
          </Typography>

          <Typography variant="body1" sx={{ mb: 2 }}>
            <strong>主題:</strong> {prompt?.topic}
          </Typography>

          {prompt && prompt.requiredWords.length > 0 ? (
            <Box sx={{ mb: 2 }}>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                請在文章中使用以下單字:
              </Typography>
              <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
                {prompt.requiredWords.map((w) => (
                  <Chip key={w.wordId} label={w.wordText} color="primary" variant="outlined" />
                ))}
              </Box>
            </Box>
          ) : (
            <Alert severity="info" sx={{ mb: 2 }}>
              這次是開放主題寫作，沒有指定必用單字，自由發揮即可。
            </Alert>
          )}

          <TextField
            fullWidth
            multiline
            rows={6}
            value={submission}
            onChange={(e) => setSubmission(e.target.value)}
            placeholder="請在此輸入你的文章..."
            disabled={isEvaluating}
            sx={{ mb: 2 }}
          />

          <Box>
            <Button
              variant="contained"
              onClick={handleSubmit}
              disabled={!submission.trim() || isEvaluating}
            >
              {isEvaluating ? '評分中...' : '提交批改'}
            </Button>
          </Box>
          {gradingError && (
            <Alert severity="error" sx={{ mt: 2 }}>
              {gradingError}
            </Alert>
          )}
        </CardContent>
      </Card>

      {evaluationResult && (
        <Card sx={{ mb: 2 }}>
          <CardContent>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 2 }}>
              <Typography variant="h6">評分結果</Typography>
              <Chip
                label={`${evaluationResult.score}分 - ${getScoreText(evaluationResult.score)}`}
                color={getScoreColor(evaluationResult.score)}
              />
            </Box>

            {srsNote && (
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 2 }}>
                {srsNote}
              </Typography>
            )}

            {evaluationResult.wordsUsed.length > 0 && (
              <Box sx={{ mb: 2 }}>
                <Typography variant="subtitle1" sx={{ mb: 1 }}>
                  單字使用狀況:
                </Typography>
                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
                  {evaluationResult.wordsUsed.map((w) => (
                    <Chip
                      key={w.word}
                      label={w.word}
                      color={w.used ? 'success' : 'default'}
                      variant={w.used ? 'filled' : 'outlined'}
                    />
                  ))}
                </Box>
              </Box>
            )}

            <Box sx={{ mb: 2 }}>
              <Accordion>
                <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                  <Typography variant="subtitle1">文法評價</Typography>
                </AccordionSummary>
                <AccordionDetails>
                  <Typography>{evaluationResult.grammarFeedback}</Typography>
                </AccordionDetails>
              </Accordion>

              <Accordion>
                <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                  <Typography variant="subtitle1">內容評價</Typography>
                </AccordionSummary>
                <AccordionDetails>
                  <Typography>{evaluationResult.contentFeedback}</Typography>
                </AccordionDetails>
              </Accordion>

              <Accordion>
                <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                  <Typography variant="subtitle1">單字運用評價</Typography>
                </AccordionSummary>
                <AccordionDetails>
                  <Typography>{evaluationResult.vocabularyFeedback}</Typography>
                </AccordionDetails>
              </Accordion>
            </Box>

            {evaluationResult.suggestions.length > 0 && (
              <Box sx={{ mb: 2 }}>
                <Typography variant="subtitle1" sx={{ mb: 1 }}>
                  改進建議:
                </Typography>
                {evaluationResult.suggestions.map((suggestion, index) => (
                  <Typography key={index} variant="body2" sx={{ mb: 0.5 }}>
                    • {suggestion}
                  </Typography>
                ))}
              </Box>
            )}

            <Box sx={{ mt: 2 }}>
              <Button variant="contained" onClick={handleNext}>
                換一題
              </Button>
            </Box>
          </CardContent>
        </Card>
      )}
    </ExamRoundShell>
  );
}

export default WritingTestCard;
