import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import LocalFireDepartmentIcon from '@mui/icons-material/LocalFireDepartment';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemText from '@mui/material/ListItemText';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { LeaderboardResponse } from '@/apis/statsApis/types';

interface LeaderboardProps {
  data: LeaderboardResponse | undefined;
  isLoading: boolean;
}

const RANK_MEDALS: Record<number, string> = {
  1: '🥇',
  2: '🥈',
  3: '🥉',
};

function Leaderboard({ data, isLoading }: LeaderboardProps) {
  if (isLoading) {
    return (
      <Box>
        {[1, 2, 3, 4, 5].map((i) => (
          <Skeleton key={i} variant="rounded" height={60} sx={{ mb: 1 }} />
        ))}
      </Box>
    );
  }

  if (!data || data.entries.length === 0) {
    return (
      <Paper sx={{ p: 3, textAlign: 'center' }}>
        <Typography variant="body1" color="text.secondary">
          No leaderboard data yet. Complete a daily challenge to appear!
        </Typography>
      </Paper>
    );
  }

  return (
    <Box>
      <List disablePadding>
        {data.entries.map((entry, idx) => (
          <Box key={idx}>
            <ListItem
              sx={{
                borderRadius: 2,
                mb: 0.5,
                bgcolor: entry.isCurrentUser ? 'primary.dark' : 'transparent',
                border: entry.isCurrentUser ? '2px solid' : 'none',
                borderColor: 'primary.main',
              }}
            >
              {/* Rank */}
              <Typography variant="h6" sx={{ width: 40, textAlign: 'center', mr: 1 }}>
                {RANK_MEDALS[entry.rank] ?? `#${entry.rank}`}
              </Typography>

              <ListItemText
                primary={
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Typography
                      variant="body1"
                      fontWeight={entry.isCurrentUser ? 'bold' : 'normal'}
                    >
                      {entry.displayName}
                      {entry.isCurrentUser && ' (You)'}
                    </Typography>
                    <Chip label={entry.level} size="small" variant="outlined" />
                  </Stack>
                }
                secondary={
                  <Stack direction="row" spacing={2} alignItems="center" sx={{ mt: 0.5 }}>
                    <Stack direction="row" spacing={0.5} alignItems="center">
                      <EmojiEventsIcon sx={{ fontSize: 16, color: '#FFD700' }} />
                      <Typography variant="caption">{entry.totalXP} XP</Typography>
                    </Stack>
                    <Stack direction="row" spacing={0.5} alignItems="center">
                      <LocalFireDepartmentIcon sx={{ fontSize: 16, color: '#FF5722' }} />
                      <Typography variant="caption">{entry.currentStreak} days</Typography>
                    </Stack>
                  </Stack>
                }
              />
            </ListItem>
            {idx < data.entries.length - 1 && <Divider />}
          </Box>
        ))}
      </List>

      {/* Current user if not in top list */}
      {data.currentUserEntry && !data.entries.some((e) => e.isCurrentUser) && (
        <Box sx={{ mt: 2 }}>
          <Divider>
            <Typography variant="caption" color="text.secondary">
              Your position
            </Typography>
          </Divider>
          <ListItem
            sx={{
              borderRadius: 2,
              mt: 1,
              bgcolor: 'primary.dark',
              border: '2px solid',
              borderColor: 'primary.main',
            }}
          >
            <Typography variant="h6" sx={{ width: 40, textAlign: 'center', mr: 1 }}>
              #{data.currentUserEntry.rank}
            </Typography>
            <ListItemText
              primary={`${data.currentUserEntry.displayName} (You)`}
              secondary={`${data.currentUserEntry.totalXP} XP • ${data.currentUserEntry.currentStreak} day streak`}
            />
          </ListItem>
        </Box>
      )}

      <Typography variant="body2" color="text.secondary" textAlign="center" sx={{ mt: 2 }}>
        {data.totalUsers} active learner{data.totalUsers !== 1 ? 's' : ''}
      </Typography>
    </Box>
  );
}

export default Leaderboard;
