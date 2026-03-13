import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import LocalFireDepartmentIcon from '@mui/icons-material/LocalFireDepartment';
import MenuBookIcon from '@mui/icons-material/MenuBook';
import QuizIcon from '@mui/icons-material/Quiz';
import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';

import { WeeklyReportResponse } from '@/apis/statsApis/types';

interface WeeklyReportProps {
  data: WeeklyReportResponse | undefined;
  isLoading: boolean;
}

interface StatCardProps {
  icon: React.ReactNode;
  label: string;
  value: string | number;
  color: string;
}

function StatCard({ icon, label, value, color }: StatCardProps) {
  return (
    <Paper sx={{ p: 2, textAlign: 'center', borderRadius: 2 }}>
      <Box sx={{ color, mb: 0.5 }}>{icon}</Box>
      <Typography variant="h5" fontWeight="bold">
        {value}
      </Typography>
      <Typography variant="body2" color="text.secondary">
        {label}
      </Typography>
    </Paper>
  );
}

function WeeklyReport({ data, isLoading }: WeeklyReportProps) {
  if (isLoading) {
    return (
      <Grid container spacing={2}>
        {[1, 2, 3, 4, 5, 6].map((i) => (
          <Grid item xs={6} sm={4} key={i}>
            <Skeleton variant="rounded" height={120} />
          </Grid>
        ))}
      </Grid>
    );
  }

  if (!data) return null;

  const accuracy =
    data.totalAnswersThisWeek > 0
      ? Math.round((data.correctAnswersThisWeek / data.totalAnswersThisWeek) * 100)
      : 0;

  return (
    <Box>
      <Typography variant="body2" color="text.secondary" textAlign="center" sx={{ mb: 2 }}>
        {data.weekStart} ~ {data.weekEnd}
      </Typography>

      <Grid container spacing={2}>
        <Grid item xs={6} sm={4}>
          <StatCard
            icon={<LocalFireDepartmentIcon sx={{ fontSize: 36 }} />}
            label="Current Streak"
            value={`${data.currentStreak} days`}
            color="#FF5722"
          />
        </Grid>
        <Grid item xs={6} sm={4}>
          <StatCard
            icon={<EmojiEventsIcon sx={{ fontSize: 36 }} />}
            label="XP This Week"
            value={`+${data.xpEarnedThisWeek}`}
            color="#FFD700"
          />
        </Grid>
        <Grid item xs={6} sm={4}>
          <StatCard
            icon={<QuizIcon sx={{ fontSize: 36 }} />}
            label="Challenges Done"
            value={`${data.challengesCompletedThisWeek}/7`}
            color="#2196F3"
          />
        </Grid>
        <Grid item xs={6} sm={4}>
          <StatCard
            icon={<Typography variant="h5">🎯</Typography>}
            label="Accuracy"
            value={`${accuracy}%`}
            color="#4CAF50"
          />
        </Grid>
        <Grid item xs={6} sm={4}>
          <StatCard
            icon={<MenuBookIcon sx={{ fontSize: 36 }} />}
            label="New Words"
            value={data.newWordsThisWeek}
            color="#9C27B0"
          />
        </Grid>
        <Grid item xs={6} sm={4}>
          <StatCard
            icon={<Typography variant="h5">🧠</Typography>}
            label="Words Reviewed"
            value={data.wordsReviewedThisWeek}
            color="#00BCD4"
          />
        </Grid>
      </Grid>

      {/* Percentile */}
      <Paper sx={{ mt: 2, p: 2, textAlign: 'center', borderRadius: 2, bgcolor: 'primary.dark' }}>
        <Typography variant="h6" color="white">
          You&apos;re more active than{' '}
          <Typography component="span" variant="h5" fontWeight="bold" color="#FFD700">
            {data.percentile}%
          </Typography>{' '}
          of learners!
        </Typography>
      </Paper>
    </Box>
  );
}

export default WeeklyReport;
