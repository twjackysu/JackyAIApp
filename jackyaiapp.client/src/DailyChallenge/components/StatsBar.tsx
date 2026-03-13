import LocalFireDepartmentIcon from '@mui/icons-material/LocalFireDepartment';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import LinearProgress from '@mui/material/LinearProgress';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

interface StatsBarProps {
  currentStreak: number;
  totalXP: number;
  level: string;
  isLoading: boolean;
}

// XP thresholds for level progress bar
const LEVEL_THRESHOLDS = [
  { min: 0, max: 100, label: 'Beginner 🌱' },
  { min: 100, max: 500, label: 'Learner 📖' },
  { min: 500, max: 2000, label: 'Explorer 🧭' },
  { min: 2000, max: 5000, label: 'Advanced 🎯' },
  { min: 5000, max: 10000, label: 'Master 👑' },
];

function StatsBar({ currentStreak, totalXP, level, isLoading }: StatsBarProps) {
  if (isLoading) {
    return <Skeleton variant="rounded" height={80} />;
  }

  // Calculate progress within current level
  const currentLevel =
    LEVEL_THRESHOLDS.find((l) => totalXP >= l.min && totalXP < l.max) ??
    LEVEL_THRESHOLDS[LEVEL_THRESHOLDS.length - 1];
  const progress = Math.min(
    ((totalXP - currentLevel.min) / (currentLevel.max - currentLevel.min)) * 100,
    100,
  );

  const streakColor = currentStreak >= 30 ? '#00BCD4' : currentStreak >= 7 ? '#FF5722' : '#FFA726';

  return (
    <Box
      sx={{
        p: 2,
        borderRadius: 2,
        bgcolor: 'background.paper',
        boxShadow: 1,
        mb: 2,
      }}
    >
      <Stack
        direction="row"
        spacing={3}
        alignItems="center"
        justifyContent="center"
        flexWrap="wrap"
      >
        {/* Streak */}
        <Stack direction="row" alignItems="center" spacing={0.5}>
          <LocalFireDepartmentIcon sx={{ color: streakColor, fontSize: 32 }} />
          <Typography variant="h5" fontWeight="bold" color={streakColor}>
            {currentStreak}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            day streak
          </Typography>
        </Stack>

        {/* Level */}
        <Chip label={level} color="primary" variant="outlined" />

        {/* XP */}
        <Stack spacing={0.5} sx={{ minWidth: 150 }}>
          <Typography variant="body2" color="text.secondary" textAlign="center">
            {totalXP} XP
          </Typography>
          <LinearProgress
            variant="determinate"
            value={progress}
            sx={{
              height: 8,
              borderRadius: 4,
              bgcolor: 'grey.800',
              '& .MuiLinearProgress-bar': {
                borderRadius: 4,
                background: 'linear-gradient(90deg, #4CAF50, #8BC34A)',
              },
            }}
          />
        </Stack>
      </Stack>
    </Box>
  );
}

export default StatsBar;
