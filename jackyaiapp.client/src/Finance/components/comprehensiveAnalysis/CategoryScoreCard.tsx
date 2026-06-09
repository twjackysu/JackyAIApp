import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import LinearProgress from '@mui/material/LinearProgress';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import {
  getDirectionLabel,
  getDirectionChipColor,
  getCategoryLabel,
  getCategoryEmoji,
  getScoreColor,
} from '../../utils/financeHelpers';

import type { CategoryScore } from '@/apis/financeApis/types';

export const CategoryScoreCard = ({ cs }: { cs: CategoryScore }) => {
  const color = getScoreColor(cs.score);
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack spacing={1.5}>
          <Stack
            direction="row"
            sx={{
              justifyContent: 'space-between',
              alignItems: 'center',
            }}
          >
            <Typography
              variant="h6"
              sx={{
                fontWeight: 'bold',
              }}
            >
              {getCategoryEmoji(cs.category)} {getCategoryLabel(cs.category)}
            </Typography>
            <Chip
              label={getDirectionLabel(cs.direction)}
              color={getDirectionChipColor(cs.direction)}
              size="small"
            />
          </Stack>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
            <Typography
              variant="h4"
              sx={{
                fontWeight: 'bold',
                color,
              }}
            >
              {Math.round(cs.score)}
            </Typography>
            <Box sx={{ flexGrow: 1 }}>
              <LinearProgress
                variant="determinate"
                value={cs.score}
                sx={{ height: 10, borderRadius: 5, '& .MuiLinearProgress-bar': { bgcolor: color } }}
              />
            </Box>
          </Box>
          <Typography variant="body2" color="text.secondary">
            {cs.summary}
          </Typography>
          <Stack
            direction="row"
            sx={{
              justifyContent: 'space-between',
            }}
          >
            <Typography variant="caption" color="text.secondary">
              權重: {(cs.weight * 100).toFixed(0)}%
            </Typography>
            <Typography variant="caption" color="text.secondary">
              加權分數: {cs.weightedScore.toFixed(1)}
            </Typography>
          </Stack>
        </Stack>
      </CardContent>
    </Card>
  );
};
