import VolumeUpIcon from '@mui/icons-material/VolumeUp';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';

import { useGetListeningResponseTestQuery, useRecordExamResultMutation } from '@/apis/examApis';
import { CREDIT_COSTS } from '@/constants/creditCosts';
import ListeningQuizCard from '@/Exam/components/ListeningQuizCard';

/** Sentinel key for the prompt clip in the card's one-shot audio history — the other keys are
 * option ids, so it only has to be something no option id can be. */
const PROMPT_KEY = '__prompt__';

/** TOEIC Part 2: hear a question/statement, pick the best spoken response. */
function ListeningResponseTestCard() {
  const { data, isFetching, refetch, isError, error } = useGetListeningResponseTestQuery();
  const [recordExamResult] = useRecordExamResultMutation();

  const test = data?.data;

  return (
    <ListeningQuizCard
      question={test}
      isFetching={isFetching}
      isError={isError}
      error={error}
      refetch={refetch}
      legend="選出最適合回應題目的選項:"
      creditCost={CREDIT_COSTS.LISTENING_RESPONSE_TEST}
      audioUrlForOption={(optionId) =>
        `/api/exam/listening/response/option-audio?optionId=${encodeURIComponent(optionId)}`
      }
      onAnswer={(questionId, correct) => {
        recordExamResult({
          examType: 'ListeningResponse',
          listeningResponseTestId: questionId,
          correct,
        })
          .unwrap()
          .catch(() => {
            /* recording is best-effort; ignore failures */
          });
      }}
      renderPrompt={({ playingKey, playedKeys, play }, submitted) => (
        <Paper
          variant="outlined"
          sx={{ p: 2, mb: 2, display: 'flex', alignItems: 'center', gap: 1 }}
        >
          <IconButton
            onClick={() => {
              if (!test) return;
              play(
                PROMPT_KEY,
                `/api/exam/listening/response/prompt-audio?testId=${encodeURIComponent(test.id)}`,
              );
            }}
            disabled={playingKey === PROMPT_KEY || (!submitted && playedKeys.has(PROMPT_KEY))}
            aria-label="播放題目"
          >
            <VolumeUpIcon
              color={playingKey === PROMPT_KEY ? 'primary' : 'inherit'}
              fontSize="large"
            />
          </IconButton>
          <Typography variant="body1" color={submitted ? 'text.primary' : 'text.secondary'}>
            {submitted ? test?.prompt : '點擊播放，聽題目後選出最適合的回應'}
          </Typography>
        </Paper>
      )}
    />
  );
}

export default ListeningResponseTestCard;
