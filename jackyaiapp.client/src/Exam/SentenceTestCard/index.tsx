import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import {
  useGetSentenceTestQuery,
  useEvaluateSentenceMutation,
  useRecordExamResultMutation,
} from '@/apis/examApis';
import AILoading from '@/components/AILoading';
import FetchBaseQueryErrorMessage from '@/components/FetchBaseQueryErrorMessage';
import { CREDIT_COSTS } from '@/constants/creditCosts';

interface EvaluationResult {
  score: number;
  grammarFeedback: string;
  usageFeedback: string;
  creativityFeedback: string;
  overallFeedback: string;
  suggestions: string[];
}

function SentenceTestCard() {
  const [userSentence, setUserSentence] = useState('');
  const [evaluationResult, setEvaluationResult] = useState<EvaluationResult | null>(null);
  const [showSample, setShowSample] = useState(false);

  const { data, isFetching, refetch, isError, error } = useGetSentenceTestQuery();
  const [evaluateSentence, { isLoading: isEvaluating }] = useEvaluateSentenceMutation();
  const [recordExamResult] = useRecordExamResultMutation();
  const [srsNote, setSrsNote] = useState<string | null>(null);

  const handleSubmit = async () => {
    if (!userSentence.trim() || !data?.data) return;

    try {
      const result = await evaluateSentence({
        word: data.data.word,
        prompt: data.data.prompt,
        context: data.data.context,
        userSentence: userSentence.trim(),
        difficultyLevel: data.data.difficultyLevel,
        grammarPattern: data.data.grammarPattern,
      }).unwrap();

      setEvaluationResult(result.data);

      // Feed the score back into spaced repetition (free). Pass ≥60 as correct.
      const wordId = data.data.wordId;
      const score = result.data.score;
      if (wordId) {
        const correct = score >= 60;
        recordExamResult({ wordId, examType: 'Sentence', correct, score })
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
    } catch (error) {
      console.error('Error evaluating sentence:', error);
    }
  };

  const handleNext = () => {
    setUserSentence('');
    setEvaluationResult(null);
    setSrsNote(null);
    setShowSample(false);
    refetch();
  };

  const getScoreColor = (score: number) => {
    if (score >= 90) return 'success';
    if (score >= 80) return 'info';
    if (score >= 70) return 'warning';
    return 'error';
  };

  const getScoreText = (score: number) => {
    if (score >= 90) return '優秀';
    if (score >= 80) return '良好';
    if (score >= 70) return '尚可';
    if (score >= 60) return '及格';
    return '需要改進';
  };

  if (isFetching) {
    return <AILoading />;
  }

  if (isError) {
    return <FetchBaseQueryErrorMessage error={error} />;
  }

  return (
    <Box sx={{ maxWidth: 800, margin: 'auto', padding: 2 }}>
      <Card sx={{ mb: 2 }}>
        <CardContent>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 2 }}>
            <Typography variant="h6" component="h2">
              造句練習
            </Typography>
            <Chip label={`難度 ${data?.data.difficultyLevel}/5`} color="primary" size="small" />
          </Box>

          <Typography variant="body1" sx={{ mb: 2, fontWeight: 'bold' }}>
            目標單字: <span style={{ color: 'primary.main' }}>{data?.data.word}</span>
          </Typography>

          <Typography variant="body1" sx={{ mb: 2 }}>
            <strong>題目:</strong> {data?.data.prompt}
          </Typography>

          <Typography variant="body2" sx={{ mb: 2, color: 'text.secondary' }}>
            <strong>情境:</strong> {data?.data.context}
          </Typography>

          {data?.data.grammarPattern && (
            <Typography variant="body2" sx={{ mb: 2, color: 'text.secondary' }}>
              <strong>語法提示:</strong> {data.data.grammarPattern}
            </Typography>
          )}

          <TextField
            fullWidth
            multiline
            rows={3}
            value={userSentence}
            onChange={(e) => setUserSentence(e.target.value)}
            placeholder="請在此輸入你的造句..."
            disabled={isEvaluating}
            sx={{ mb: 2 }}
          />

          <Box sx={{ display: 'flex', gap: 2, mb: 1, alignItems: 'flex-end' }}>
            <Box>
              <Button
                variant="contained"
                onClick={handleSubmit}
                disabled={!userSentence.trim() || isEvaluating}
              >
                {isEvaluating ? '評分中...' : '提交答案'}
              </Button>
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{ display: 'block', mt: 0.5 }}
              >
                AI 評分消耗 {CREDIT_COSTS.SENTENCE_EVALUATION} credits
              </Typography>
            </Box>
            <Button variant="outlined" onClick={() => setShowSample(!showSample)} sx={{ mb: 0.5 }}>
              {showSample ? '隱藏' : '顯示'}範例答案
            </Button>
          </Box>

          {showSample && (
            <Alert severity="info" sx={{ mb: 2 }}>
              <AlertTitle>範例答案</AlertTitle>
              {data?.data.sampleAnswer}
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

            <Alert severity={evaluationResult.score >= 70 ? 'success' : 'warning'} sx={{ mb: 2 }}>
              <AlertTitle>總體評價</AlertTitle>
              {evaluationResult.overallFeedback}
            </Alert>

            {srsNote && (
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 2 }}>
                {srsNote}
              </Typography>
            )}

            <Box sx={{ mb: 2 }}>
              <Accordion>
                <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                  <Typography variant="subtitle1">語法評價</Typography>
                </AccordionSummary>
                <AccordionDetails>
                  <Typography>{evaluationResult.grammarFeedback}</Typography>
                </AccordionDetails>
              </Accordion>

              <Accordion>
                <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                  <Typography variant="subtitle1">用詞評價</Typography>
                </AccordionSummary>
                <AccordionDetails>
                  <Typography>{evaluationResult.usageFeedback}</Typography>
                </AccordionDetails>
              </Accordion>

              <Accordion>
                <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                  <Typography variant="subtitle1">創意表達</Typography>
                </AccordionSummary>
                <AccordionDetails>
                  <Typography>{evaluationResult.creativityFeedback}</Typography>
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
                下一題
              </Button>
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{ display: 'block', mt: 0.5 }}
              >
                下一題消耗 {CREDIT_COSTS.SENTENCE_TEST} credits
              </Typography>
            </Box>
          </CardContent>
        </Card>
      )}
    </Box>
  );
}

export default SentenceTestCard;
