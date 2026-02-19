import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Typography from '@mui/material/Typography';

import { getScoreColor } from '../utils/financeHelpers';

interface ScoreGaugeProps {
  score: number;
  size?: number;
  label?: string;
}

export const ScoreGauge = ({ score, size = 120, label }: ScoreGaugeProps) => {
  const color = getScoreColor(score);
  return (
    <Box sx={{ position: 'relative', display: 'inline-flex' }}>
      <CircularProgress variant="determinate" value={100} size={size} thickness={4} sx={{ color: 'grey.200', position: 'absolute' }} />
      <CircularProgress variant="determinate" value={score} size={size} thickness={4} sx={{ color }} />
      <Box sx={{ top: 0, left: 0, bottom: 0, right: 0, position: 'absolute', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center' }}>
        <Typography variant="h4" fontWeight="bold" sx={{ color, lineHeight: 1 }}>{Math.round(score)}</Typography>
        {label && <Typography variant="caption" color="text.secondary">{label}</Typography>}
      </Box>
    </Box>
  );
};

export default ScoreGauge;
