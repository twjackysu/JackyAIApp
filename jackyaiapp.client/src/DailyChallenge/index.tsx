import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import SendIcon from '@mui/icons-material/Send';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import MobileStepper from '@mui/material/MobileStepper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useState, useCallback, useMemo } from 'react';

import {
  useGetChallengeQuery,
  useSubmitChallengeMutation,
  useGetStatsQuery,
} from '@/apis/dailyChallengeApis';
import { DailyChallengeSubmitResponse } from '@/apis/dailyChallengeApis/types';

import QuestionCard from './components/QuestionCard';
import ResultScreen from './components/ResultScreen';
import StatsBar from './components/StatsBar';

type ViewMode = 'challenge' | 'result' | 'review';

function DailyChallenge() {
  const {
    data: challengeData,
    isLoading: isChallengeLoading,
    error: challengeError,
  } = useGetChallengeQuery();
  const { data: statsData, isLoading: isStatsLoading } = useGetStatsQuery();
  const [submitChallenge, { isLoading: isSubmitting }] = useSubmitChallengeMutation();

  const [currentQuestion, setCurrentQuestion] = useState(0);
  const [answers, setAnswers] = useState<Record<number, number>>({});
  const [viewMode, setViewMode] = useState<ViewMode>('challenge');
  const [submitResult, setSubmitResult] = useState<DailyChallengeSubmitResponse | null>(null);

  const challenge = challengeData?.data;
  const stats = statsData?.data;
  const questions = useMemo(() => challenge?.questions ?? [], [challenge?.questions]);
  const totalQuestions = questions.length;

  // If already completed, show result immediately
  const alreadyCompleted = challenge?.alreadyCompleted ?? false;

  const handleSelect = useCallback(
    (index: number) => {
      setAnswers((prev) => ({
        ...prev,
        [questions[currentQuestion]?.id]: index,
      }));
    },
    [currentQuestion, questions],
  );

  const handleNext = useCallback(() => {
    if (currentQuestion < totalQuestions - 1) {
      setCurrentQuestion((prev) => prev + 1);
    }
  }, [currentQuestion, totalQuestions]);

  const handleBack = useCallback(() => {
    if (currentQuestion > 0) {
      setCurrentQuestion((prev) => prev - 1);
    }
  }, [currentQuestion]);

  const handleSubmit = useCallback(async () => {
    const answerList = questions.map((q) => ({
      questionId: q.id,
      selectedIndex: answers[q.id] ?? -1,
    }));

    try {
      const result = await submitChallenge({ answers: answerList }).unwrap();
      setSubmitResult(result.data);
      setViewMode('result');
    } catch (err) {
      console.error('Failed to submit challenge:', err);
    }
  }, [answers, questions, submitChallenge]);

  const handleReview = useCallback(() => {
    setCurrentQuestion(0);
    setViewMode('review');
  }, []);

  const allAnswered = questions.every((q) => answers[q.id] !== undefined);

  // Loading state
  if (isChallengeLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', mt: 8 }}>
        <CircularProgress />
      </Box>
    );
  }

  // Error state
  if (challengeError) {
    return (
      <Box sx={{ maxWidth: 600, mx: 'auto', mt: 4 }}>
        <Alert severity="error">Failed to load today's challenge. Please try again later.</Alert>
      </Box>
    );
  }

  return (
    <Box sx={{ maxWidth: 700, mx: 'auto', mt: 2 }}>
      {/* Header */}
      <Typography variant="h4" fontWeight="bold" textAlign="center" sx={{ mb: 2 }}>
        🎯 Daily Challenge
      </Typography>
      <Typography variant="body2" color="text.secondary" textAlign="center" sx={{ mb: 2 }}>
        {challenge?.challengeDate}
      </Typography>

      {/* Stats bar */}
      <StatsBar
        currentStreak={stats?.currentStreak ?? 0}
        totalXP={stats?.totalXP ?? 0}
        level={stats?.level ?? 'Beginner 🌱'}
        isLoading={isStatsLoading}
      />

      {/* Already completed notice */}
      {alreadyCompleted && viewMode === 'challenge' && (
        <Alert severity="success" sx={{ mb: 2 }}>
          ✅ You've already completed today's challenge! Score: {challenge?.previousScore}/
          {totalQuestions}. Come back tomorrow for a new challenge!
        </Alert>
      )}

      {/* Result screen */}
      {viewMode === 'result' && submitResult && (
        <ResultScreen
          score={submitResult.score}
          totalQuestions={submitResult.totalQuestions}
          xpEarned={submitResult.xpEarned}
          newStreak={submitResult.newStreak}
          alreadyCompleted={submitResult.alreadyCompleted}
          onReview={handleReview}
        />
      )}

      {/* Challenge questions or review */}
      {(viewMode === 'challenge' || viewMode === 'review') && questions.length > 0 && (
        <>
          <QuestionCard
            question={questions[currentQuestion]}
            questionNumber={currentQuestion + 1}
            totalQuestions={totalQuestions}
            selectedIndex={answers[questions[currentQuestion].id] ?? null}
            onSelect={handleSelect}
            showResult={viewMode === 'review' || alreadyCompleted}
          />

          {/* Navigation */}
          <MobileStepper
            variant="dots"
            steps={totalQuestions}
            position="static"
            activeStep={currentQuestion}
            sx={{ mt: 2, bgcolor: 'transparent', justifyContent: 'center' }}
            backButton={
              <Button size="small" onClick={handleBack} disabled={currentQuestion === 0}>
                <ArrowBackIcon />
                Back
              </Button>
            }
            nextButton={
              currentQuestion === totalQuestions - 1 &&
              viewMode === 'challenge' &&
              !alreadyCompleted ? (
                <Button
                  size="small"
                  onClick={handleSubmit}
                  disabled={!allAnswered || isSubmitting}
                  color="success"
                  variant="contained"
                >
                  {isSubmitting ? (
                    <CircularProgress size={20} />
                  ) : (
                    <>
                      <SendIcon sx={{ mr: 0.5 }} /> Submit
                    </>
                  )}
                </Button>
              ) : (
                <Button
                  size="small"
                  onClick={handleNext}
                  disabled={currentQuestion === totalQuestions - 1}
                >
                  Next
                  <ArrowForwardIcon />
                </Button>
              )
            }
          />

          {/* Quick nav hint */}
          {viewMode === 'challenge' && !alreadyCompleted && (
            <Stack direction="row" justifyContent="center" sx={{ mt: 1 }}>
              <Typography variant="caption" color="text.secondary">
                Answer all {totalQuestions} questions, then submit
              </Typography>
            </Stack>
          )}
        </>
      )}
    </Box>
  );
}

export default DailyChallenge;
