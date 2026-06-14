import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Paper from '@mui/material/Paper';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useMemo } from 'react';

import type { ActivityDay } from '@/apis/statsApis/types';

import { useGetActivityQuery } from '@/apis/statsApis';

// GitHub-style green scale (works on light and dark surfaces).
const EMPTY = 'rgba(127, 127, 127, 0.12)';
const LEVELS = ['#0e4429', '#006d32', '#26a641', '#39d353'];

const CELL = 11;
const GAP = 3;

function levelColor(count: number): string {
  if (count <= 0) return EMPTY;
  if (count === 1) return LEVELS[0];
  if (count === 2) return LEVELS[1];
  if (count <= 4) return LEVELS[2];
  return LEVELS[3];
}

interface Cell {
  day: ActivityDay | null; // null = padding before the first day
}

/**
 * Calendar heatmap of learning activity (daily challenges + exam attempts).
 * Self-motivating, zero-nag — there's nothing to dismiss or lose.
 */
function ActivityHeatmap() {
  const { data, isLoading } = useGetActivityQuery(365);
  const days = useMemo(() => data?.data?.days ?? [], [data]);
  const activeDays = data?.data?.activeDays ?? 0;

  // Build week columns: pad the start so the first day lands on its weekday row (Sun=0).
  const columns = useMemo<Cell[][]>(() => {
    if (days.length === 0) return [];
    const cells: Cell[] = [];
    const firstWeekday = new Date(days[0].date + 'T00:00:00Z').getUTCDay();
    for (let i = 0; i < firstWeekday; i++) cells.push({ day: null });
    for (const d of days) cells.push({ day: d });
    const cols: Cell[][] = [];
    for (let i = 0; i < cells.length; i += 7) cols.push(cells.slice(i, i + 7));
    return cols;
  }, [days]);

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
        <CircularProgress />
      </Box>
    );
  }

  return (
    <Paper variant="outlined" sx={{ p: 2, borderRadius: 3 }}>
      <Box
        sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: 1.5 }}
      >
        <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
          Activity
        </Typography>
        <Typography variant="caption" color="text.secondary">
          {activeDays} active {activeDays === 1 ? 'day' : 'days'} in the last year
        </Typography>
      </Box>

      <Box sx={{ overflowX: 'auto', pb: 1 }}>
        <Box sx={{ display: 'flex', gap: `${GAP}px`, width: 'max-content' }}>
          {columns.map((col, ci) => (
            <Box key={ci} sx={{ display: 'flex', flexDirection: 'column', gap: `${GAP}px` }}>
              {col.map((cell, ri) =>
                cell.day ? (
                  <Tooltip
                    key={ri}
                    title={`${cell.day.count} on ${cell.day.date}`}
                    placement="top"
                    arrow
                  >
                    <Box
                      sx={{
                        width: CELL,
                        height: CELL,
                        borderRadius: '2px',
                        bgcolor: levelColor(cell.day.count),
                      }}
                    />
                  </Tooltip>
                ) : (
                  <Box key={ri} sx={{ width: CELL, height: CELL }} />
                ),
              )}
            </Box>
          ))}
        </Box>
      </Box>

      {/* Legend */}
      <Box
        sx={{ display: 'flex', alignItems: 'center', gap: 0.5, mt: 1, justifyContent: 'flex-end' }}
      >
        <Typography variant="caption" color="text.secondary" sx={{ mr: 0.5 }}>
          Less
        </Typography>
        <Box sx={{ width: CELL, height: CELL, borderRadius: '2px', bgcolor: EMPTY }} />
        {LEVELS.map((c) => (
          <Box key={c} sx={{ width: CELL, height: CELL, borderRadius: '2px', bgcolor: c }} />
        ))}
        <Typography variant="caption" color="text.secondary" sx={{ ml: 0.5 }}>
          More
        </Typography>
      </Box>
    </Paper>
  );
}

export default ActivityHeatmap;
