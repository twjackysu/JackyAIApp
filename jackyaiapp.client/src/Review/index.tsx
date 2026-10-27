import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useState, useCallback, useMemo, useEffect, useRef } from 'react';

import Flashcard from './components/Flashcard';

import { useGetDueReviewsQuery, useSubmitReviewsMutation } from '@/apis/reviewApis';
import { ReviewAnswerRequest, ReviewSubmitResponse } from '@/apis/reviewApis/types';
import { getCaughtErrorMessage } from '@/apis/utils/errorMessage';
import CenteredSpinner from '@/components/CenteredSpinner';
import { readJson, removeKey, writeJson } from '@/utils/storage';

const REVIEW_STORAGE_KEY = 'review_progress';

interface ReviewProgress {
  wordIds: number[];
  currentIndex: number;
  ratings: ReviewAnswerRequest[];
}

/** When the server gave no reason a reader can use. */
const REVIEW_NOT_SAVED = '這一輪的評分沒有存成功。';
const RATE_AGAIN_NOTE = '再評一次這張卡片，就會把整輪重新送出。';

function Review() {
  const { data: dueData, isLoading, error } = useGetDueReviewsQuery();
  const [submitReviews, { isLoading: isSubmitting }] = useSubmitReviewsMutation();

  const [currentIndex, setCurrentIndex] = useState(0);
  const [ratings, setRatings] = useState<ReviewAnswerRequest[]>([]);
  const [result, setResult] = useState<ReviewSubmitResponse | null>(null);
  // Why the round's ratings were not saved. The last card stays, so rating it again resends them all.
  const [submitError, setSubmitError] = useState<string | null>(null);
  const restoredRef = useRef(false);

  const dueWords = useMemo(() => dueData?.data?.dueWords ?? [], [dueData?.data?.dueWords]);
  const totalDue = dueData?.data?.totalDueCount ?? 0;

  // Restore progress from localStorage once words are loaded
  useEffect(() => {
    if (dueWords.length === 0 || restoredRef.current) return;
    restoredRef.current = true;

    const saved = readJson<ReviewProgress>(REVIEW_STORAGE_KEY);
    if (!saved) return;

    const currentWordIds = dueWords.map((w) => w.userWordId);
    // Only restore if the word set matches and progress is still valid
    if (
      JSON.stringify(saved.wordIds) === JSON.stringify(currentWordIds) &&
      saved.currentIndex < dueWords.length
    ) {
      setCurrentIndex(saved.currentIndex);

      setRatings(saved.ratings);
    }
  }, [dueWords]);

  // Save progress to localStorage on each change
  useEffect(() => {
    if (dueWords.length === 0 || result) return;
    if (currentIndex === 0 && ratings.length === 0) return; // skip initial state
    const progress: ReviewProgress = {
      wordIds: dueWords.map((w) => w.userWordId),
      currentIndex,
      ratings,
    };
    writeJson(REVIEW_STORAGE_KEY, progress);
  }, [currentIndex, ratings, dueWords, result]);

  const handleRate = useCallback(
    (quality: number) => {
      const word = dueWords[currentIndex];
      if (!word) return;

      const newRating: ReviewAnswerRequest = { userWordId: word.userWordId, quality };

      if (currentIndex < dueWords.length - 1) {
        setRatings((prev) => [...prev, newRating]);
        setCurrentIndex((prev) => prev + 1);
      } else {
        // All reviewed — submit
        const allRatings = [...ratings, newRating];
        setSubmitError(null);
        submitReviews({ reviews: allRatings })
          .unwrap()
          .then((res) => {
            removeKey(REVIEW_STORAGE_KEY);
            setResult(res.data);
          })
          .catch((err) => setSubmitError(getCaughtErrorMessage(err, REVIEW_NOT_SAVED)));
      }
    },
    [currentIndex, dueWords, ratings, submitReviews],
  );

  // Loading
  if (isLoading) {
    return <CenteredSpinner sx={{ mt: 8 }} />;
  }

  // Error
  if (error) {
    return (
      <Box sx={{ maxWidth: 600, mx: 'auto', mt: 4 }}>
        <Alert severity="error">Failed to load reviews. Please try again later.</Alert>
      </Box>
    );
  }

  // No words due
  if (dueWords.length === 0 && !result) {
    return (
      <Box sx={{ maxWidth: 600, mx: 'auto', mt: 4, textAlign: 'center' }}>
        <Typography variant="h1" sx={{ mb: 2 }}>
          ✅
        </Typography>
        <Typography
          variant="h5"
          sx={{
            fontWeight: 'bold',
            mb: 1,
          }}
        >
          All caught up!
        </Typography>
        <Typography variant="body1" color="text.secondary">
          No words due for review right now. Add more words to your repository or check back later!
        </Typography>
      </Box>
    );
  }

  // Result screen
  if (result) {
    const percentage = Math.round((result.correctCount / result.wordsReviewed) * 100);
    return (
      <Paper sx={{ maxWidth: 500, mx: 'auto', mt: 4, p: 4, borderRadius: 3, textAlign: 'center' }}>
        <Typography variant="h1" sx={{ mb: 1 }}>
          🧠
        </Typography>
        <Typography
          variant="h5"
          sx={{
            fontWeight: 'bold',
            mb: 2,
          }}
        >
          Review Complete!
        </Typography>
        <Typography
          variant="h3"
          color="primary.main"
          sx={{
            fontWeight: 'bold',
            mb: 2,
          }}
        >
          {result.correctCount} / {result.wordsReviewed}
        </Typography>
        <Typography variant="body1" color="text.secondary" sx={{ mb: 2 }}>
          {percentage}% retention rate
        </Typography>
        <Stack
          direction="row"
          spacing={2}
          sx={{
            justifyContent: 'center',
            mb: 2,
          }}
        >
          <Box
            sx={{
              textAlign: 'center',
            }}
          >
            <EmojiEventsIcon sx={{ color: '#FFD700', fontSize: 32 }} />
            <Typography
              variant="h6"
              color="#FFD700"
              sx={{
                fontWeight: 'bold',
              }}
            >
              +{result.xpEarned} XP
            </Typography>
          </Box>
        </Stack>
        {totalDue > dueWords.length && (
          <Button variant="outlined" onClick={() => window.location.reload()} sx={{ mt: 1 }}>
            Review more ({totalDue - dueWords.length} remaining)
          </Button>
        )}
      </Paper>
    );
  }

  // Active review
  return (
    <Box sx={{ maxWidth: 700, mx: 'auto', mt: 2 }}>
      <Typography
        variant="body2"
        color="text.secondary"
        sx={{
          textAlign: 'center',
          mb: 3,
        }}
      >
        {totalDue} word{totalDue !== 1 ? 's' : ''} due for review
        {ratings.length > 0 && (
          <Typography component="span" color="success.main" sx={{ ml: 1 }}>
            · {ratings.length} done
          </Typography>
        )}
      </Typography>
      {submitError && !isSubmitting && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {submitError} {RATE_AGAIN_NOTE}
        </Alert>
      )}
      {isSubmitting ? (
        <CenteredSpinner sx={{ mt: 4 }} />
      ) : (
        // key={currentIndex} forces remount on card change, resetting revealed state
        <Flashcard
          key={currentIndex}
          word={dueWords[currentIndex]}
          cardNumber={currentIndex + 1}
          totalCards={dueWords.length}
          onRate={handleRate}
        />
      )}
    </Box>
  );
}

export default Review;
