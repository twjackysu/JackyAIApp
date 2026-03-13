import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import LocalFireDepartmentIcon from '@mui/icons-material/LocalFireDepartment';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

interface ResultScreenProps {
  score: number;
  totalQuestions: number;
  xpEarned: number;
  newStreak: number;
  alreadyCompleted: boolean;
  creditsAwarded: number;
  onReview: () => void;
}

function ResultScreen({
  score,
  totalQuestions,
  xpEarned,
  newStreak,
  alreadyCompleted,
  creditsAwarded,
  onReview,
}: ResultScreenProps) {
  const isPerfect = score === totalQuestions;
  const percentage = Math.round((score / totalQuestions) * 100);

  const getEmoji = () => {
    if (isPerfect) return '🎉';
    if (percentage >= 80) return '👏';
    if (percentage >= 60) return '💪';
    return '📚';
  };

  const getMessage = () => {
    if (alreadyCompleted) return "You've already completed today's challenge!";
    if (isPerfect) return 'Perfect score! Amazing!';
    if (percentage >= 80) return 'Great job! Almost perfect!';
    if (percentage >= 60) return 'Good effort! Keep learning!';
    return "Keep practicing! You'll improve!";
  };

  return (
    <Paper
      sx={{
        maxWidth: 500,
        mx: 'auto',
        p: 4,
        borderRadius: 3,
        textAlign: 'center',
        boxShadow: 3,
      }}
    >
      {/* Big emoji */}
      <Typography variant="h1" sx={{ mb: 1 }}>
        {getEmoji()}
      </Typography>

      {/* Message */}
      <Typography variant="h5" fontWeight="bold" sx={{ mb: 3 }}>
        {getMessage()}
      </Typography>

      {/* Score */}
      <Typography
        variant="h2"
        fontWeight="bold"
        color={isPerfect ? 'success.main' : percentage >= 60 ? 'primary.main' : 'warning.main'}
        sx={{ mb: 3 }}
      >
        {score} / {totalQuestions}
      </Typography>

      {/* XP + Streak + Credits */}
      {!alreadyCompleted && (
        <Stack direction="row" spacing={3} justifyContent="center" sx={{ mb: 3 }} flexWrap="wrap">
          <Box textAlign="center">
            <EmojiEventsIcon sx={{ color: '#FFD700', fontSize: 36 }} />
            <Typography variant="h6" fontWeight="bold" color="#FFD700">
              +{xpEarned} XP
            </Typography>
          </Box>
          <Box textAlign="center">
            <LocalFireDepartmentIcon sx={{ color: '#FF5722', fontSize: 36 }} />
            <Typography variant="h6" fontWeight="bold" color="#FF5722">
              {newStreak} day streak
            </Typography>
          </Box>
          {creditsAwarded > 0 && (
            <Box textAlign="center">
              <Typography variant="h4">💰</Typography>
              <Typography variant="h6" fontWeight="bold" color="#4CAF50">
                +{creditsAwarded} credits
              </Typography>
            </Box>
          )}
        </Stack>
      )}

      <Button variant="outlined" size="large" onClick={onReview} sx={{ mt: 1 }}>
        Review Answers
      </Button>
    </Paper>
  );
}

export default ResultScreen;
