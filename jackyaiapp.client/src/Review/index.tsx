import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useState, useCallback, useMemo, useEffect, useRef } from 'react';

import { useGetDueReviewsQuery, useSubmitReviewsMutation } from '@/apis/reviewApis';
import { ReviewAnswerRequest, ReviewSubmitResponse } from '@/apis/reviewApis/types';

import Flashcard from './components/Flashcard';

const REVIEW_STORAGE_KEY = 'review_progress';

interface ReviewProgress {
  wordIds: number[];
  currentIndex: number;
  ratings: ReviewAnswerRequest[];
}

function Review() {
  const { data: dueData, isLoading, error } = useGetDueReviewsQuery();
  const [submitReviews, { isLoading: isSubmitting }] = useSubmitReviewsMutation();

  const [currentIndex, setCurrentIndex] = useState(0);
  const [ratings, setRatings] = useState<ReviewAnswerRequest[]>([]);
  const [result, setResult] = useState<ReviewSubmitResponse | null>(null);
  const restoredRef = useRef(false);

  const dueWords = useMemo(() => dueData?.data?.dueWords ?? [], [dueData?.data?.dueWords]);
  const totalDue = dueData?.data?.totalDueCount ?? 0;

  // Restore progress from localStorage once words are loaded
  useEffect(() => {
    if (dueWords.length === 0 || restoredRef.current) return;
    restoredRef.current = true;

    try {
      const saved = localStorage.getItem(REVIEW_STORAGE_KEY);
      if (!saved) return;
      const { wordIds, currentIndex: savedIndex, ratings: savedRatings }: ReviewProgress =
        JSON.parse(saved);
      const currentWordIds = dueWords.map((w) => w.userWordId);
      // Only restore if the word set matches and progress is still valid
      if (
        JSON.stringify(wordIds) === JSON.stringify(currentWordIds) &&
        savedIndex < dueWords.length
      ) {
        setCurrentIndex(savedIndex);
        setRatings(savedRatings);
      }
    } catch {
      localStorage.removeItem(REVIEW_STORAGE_KEY);
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
    localStorage.setItem(REVIEW_STORAGE_KEY, JSON.stringify(progress));
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
        submitReviews({ reviews: allRatings })
          .unwrap()
          .then((res) => {
            localStorage.removeItem(REVIEW_STORAGE_KEY);
            setResult(res.data);
          })
          .catch((err) => console.error('Failed to submit reviews:', err));
      }
    },
    [currentIndex, dueWords, ratings, submitReviews],
  );

  // Loading
  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', mt: 8 }}>
        <CircularProgress />
      </Box>
    );
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
        <Typography variant="h5" fontWeight="bold" sx={{ mb: 1 }}>
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
        <Typography variant="h5" fontWeight="bold" sx={{ mb: 2 }}>
          Review Complete!
        </Typography>
        <Typography variant="h3" fontWeight="bold" color="primary.main" sx={{ mb: 2 }}>
          {result.correctCount} / {result.wordsReviewed}
        </Typography>
        <Typography variant="body1" color="text.secondary" sx={{ mb: 2 }}>
          {percentage}% retention rate
        </Typography>
        <Stack direction="row" spacing={2} justifyContent="center" sx={{ mb: 2 }}>
          <Box textAlign="center">
            <EmojiEventsIcon sx={{ color: '#FFD700', fontSize: 32 }} />
            <Typography variant="h6" fontWeight="bold" color="#FFD700">
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
      <Typography variant="body2" color="text.secondary" textAlign="center" sx={{ mb: 3 }}>
        {totalDue} word{totalDue !== 1 ? 's' : ''} due for review
        {ratings.length > 0 && (
          <Typography component="span" color="success.main" sx={{ ml: 1 }}>
            · {ratings.length} done
          </Typography>
        )}
      </Typography>

      {isSubmitting ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 4 }}>
          <CircularProgress />
        </Box>
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
