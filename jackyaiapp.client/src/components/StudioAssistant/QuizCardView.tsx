import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import CloseIcon from '@mui/icons-material/HighlightOff';
import QuizIcon from '@mui/icons-material/QuizOutlined';
import { Box, Button, ButtonBase, Stack, Typography } from '@mui/material';
import { alpha, useTheme } from '@mui/material/styles';
import { useMemo, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';

import { parseQuizCard, qualityOf, quizSummary, type QuizCardData } from './quizCard';

import type { CustomCard } from '@/components/aiChat/cards/toolCard';

import { useSubmitReviewsMutation } from '@/apis/reviewApis';
import { ACCENT, hairline } from '@/components/aiChat/style';

const LETTERS = 'ABCDEF';

/**
 * Studio AI's review quiz in the answer (`review_quiz`): one question at a time, answered with a press, the right
 * answer and its explanation shown at once; when the last is done, the answers go to the review schedule (the review
 * page's own call, so the words come back as SM-2 decides, and right answers earn XP). Nothing is asked of the AI.
 * Only a quiz on words that are due, the first time: one drawn again from a reopened conversation (`saved`) was
 * answered already, and a word not yet due would be pushed further out by an early answer, so those are practice.
 */
export default function QuizCardView({
  card,
  saved = false,
}: {
  card: CustomCard;
  saved?: boolean;
}) {
  const quiz = useMemo(() => parseQuizCard(card), [card]);
  return quiz ? <Quiz quiz={quiz} saved={saved} /> : null;
}

function Quiz({ quiz, saved }: { quiz: QuizCardData; saved: boolean }) {
  const theme = useTheme();
  const total = quiz.questions.length;
  const [index, setIndex] = useState(0);
  const [picked, setPicked] = useState<(number | null)[]>(() => quiz.questions.map(() => null));
  const [finished, setFinished] = useState(false);
  const [submit, sent] = useSubmitReviewsMutation();
  const recorded = quiz.fromDue && !saved;

  const send = () =>
    void submit({
      reviews: quiz.questions.map((q, i) => ({
        userWordId: q.userWordId,
        quality: qualityOf(picked[i] === q.correctIndex),
      })),
    });

  const header = (
    <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
      <QuizIcon sx={{ fontSize: 18, color: ACCENT }} />
      <Typography variant="subtitle2" sx={{ flex: 1 }}>
        {quiz.title}
      </Typography>
      <Typography variant="caption" color="text.secondary">
        {recorded ? '' : '練習・'}
        {finished ? `${total} 題` : `第 ${index + 1} / ${total} 題`}
      </Typography>
    </Stack>
  );

  if (finished) {
    const right = quiz.questions.filter((q, i) => picked[i] === q.correctIndex).length;
    const moreDue = quiz.fromDue ? Math.max(0, quiz.totalDue - total) : 0;
    return (
      <Box data-testid="quiz-card">
        {header}
        <Typography variant="body2" sx={{ fontWeight: 600, mb: 1 }}>
          {quizSummary(right, total)}
        </Typography>
        <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.75, mb: 1.25 }}>
          {quiz.questions.map((q, i) => {
            const ok = picked[i] === q.correctIndex;
            const Icon = ok ? CheckIcon : CloseIcon;
            return (
              <Stack
                key={`${q.userWordId}-${i}`}
                direction="row"
                spacing={0.5}
                sx={{ alignItems: 'center' }}
              >
                <Icon
                  aria-label={ok ? '答對' : '答錯'}
                  sx={{ fontSize: 16, color: ok ? 'success.main' : 'error.main' }}
                />
                <Typography variant="body2">{q.word}</Typography>
              </Stack>
            );
          })}
        </Stack>
        <Stack
          direction="row"
          spacing={1}
          sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 1 }}
        >
          <Typography variant="caption" color="text.secondary" sx={{ flex: 1, minWidth: 160 }}>
            {!recorded
              ? saved
                ? '這份測驗之前答過，這次是練習，不再記進複習排程。'
                : '這些字還沒到期，這次是練習，不記進複習排程。'
              : sent.isLoading
                ? '記進複習排程中…'
                : sent.isSuccess
                  ? `已記進複習排程${sent.data?.data?.xpEarned ? `・+${sent.data.data.xpEarned} XP` : ''}`
                  : sent.isError
                    ? '沒有記進複習排程。'
                    : ''}
          </Typography>
          {sent.isError && (
            <Button size="small" onClick={send}>
              再試一次
            </Button>
          )}
          {moreDue > 0 && (
            <Button component={RouterLink} to="/english/review" variant="outlined" size="small">
              還有 {moreDue} 個字，去複習
            </Button>
          )}
        </Stack>
      </Box>
    );
  }

  const q = quiz.questions[index];
  const chosen = picked[index];
  const answered = chosen !== null;
  const last = index === total - 1;

  return (
    <Box data-testid="quiz-card">
      {header}
      <Typography variant="body2" sx={{ mb: 1, overflowWrap: 'anywhere' }}>
        {q.prompt}
      </Typography>
      <Stack spacing={0.75} role="group" aria-label={`第 ${index + 1} 題的選項`}>
        {q.options.map((option, i) => {
          const isRight = i === q.correctIndex;
          const color = !answered
            ? null
            : isRight
              ? theme.palette.success.main
              : i === chosen
                ? theme.palette.error.main
                : null;
          return (
            <ButtonBase
              key={i}
              disabled={answered}
              onClick={() => setPicked((was) => was.map((p, j) => (j === index ? i : p)))}
              sx={{
                justifyContent: 'flex-start',
                textAlign: 'left',
                gap: 1,
                px: 1.25,
                py: 0.75,
                borderRadius: 1.5,
                border: `1px solid ${color ?? hairline(theme)}`,
                bgcolor: color ? alpha(color, 0.12) : 'transparent',
                transition: 'border-color 120ms, background-color 120ms',
                '&:hover': { borderColor: answered ? undefined : ACCENT },
                '&.Mui-disabled': { color: 'text.primary' },
              }}
            >
              <Typography
                component="span"
                variant="caption"
                sx={{ fontWeight: 700, color: color ?? 'text.secondary', minWidth: 14 }}
              >
                {LETTERS[i]}
              </Typography>
              <Typography component="span" variant="body2" sx={{ overflowWrap: 'anywhere' }}>
                {option}
              </Typography>
            </ButtonBase>
          );
        })}
      </Stack>
      {answered && (
        <Box aria-live="polite" sx={{ mt: 1 }}>
          <Typography
            variant="body2"
            sx={{
              fontWeight: 600,
              color: chosen === q.correctIndex ? 'success.main' : 'error.main',
            }}
          >
            {chosen === q.correctIndex ? '答對了！' : `答案是 ${LETTERS[q.correctIndex]}`}
          </Typography>
          {q.explanation && (
            <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
              {q.explanation}
            </Typography>
          )}
          <Box sx={{ display: 'flex', justifyContent: 'flex-end', mt: 1 }}>
            <Button
              size="small"
              variant="contained"
              onClick={() => {
                if (!last) return setIndex(index + 1);
                setFinished(true);
                if (recorded) send();
              }}
            >
              {last ? '看結果' : '下一題'}
            </Button>
          </Box>
        </Box>
      )}
    </Box>
  );
}
