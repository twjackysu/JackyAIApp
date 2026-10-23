import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';

import { BreakdownSeries } from './breakdown';

/** The series under the chart; clicking one hides or shows it. */
export function NetWorthLegend({
  breakdown,
  hiddenNames,
  onToggle,
}: {
  breakdown: BreakdownSeries[];
  hiddenNames: Set<string>;
  onToggle: (name: string) => void;
}) {
  return (
    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1.5, mt: 1, justifyContent: 'center' }}>
      {breakdown.map((s) => {
        const hidden = hiddenNames.has(s.name);
        return (
          <Box
            key={s.key}
            onClick={() => onToggle(s.name)}
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: 0.5,
              cursor: 'pointer',
              userSelect: 'none',
              opacity: hidden ? 0.4 : 1,
            }}
          >
            <Box
              sx={{
                width: 10,
                height: 10,
                borderRadius: '50%',
                bgcolor: hidden ? 'text.disabled' : s.color,
              }}
            />
            <Typography
              variant="caption"
              color="text.secondary"
              sx={{ textDecoration: hidden ? 'line-through' : 'none' }}
            >
              {s.name}
            </Typography>
          </Box>
        );
      })}
    </Box>
  );
}
