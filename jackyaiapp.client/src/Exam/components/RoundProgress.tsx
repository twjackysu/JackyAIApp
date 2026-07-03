import LinearProgress from '@mui/material/LinearProgress';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

interface RoundProgressProps {
  answered: number;
  total: number;
  correct: number;
}

function RoundProgress({ answered, total, correct }: RoundProgressProps) {
  return (
    <Stack spacing={0.5} sx={{ mb: 2 }}>
      <Typography variant="caption" color="text.secondary">
        第 {answered + 1}/{total} 題・已答對 {correct} 題
      </Typography>
      <LinearProgress
        variant="determinate"
        value={(answered / total) * 100}
        sx={{ borderRadius: 1 }}
      />
    </Stack>
  );
}

export default RoundProgress;
