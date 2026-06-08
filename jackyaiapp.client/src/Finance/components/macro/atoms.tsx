import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingFlatIcon from '@mui/icons-material/TrendingFlat';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import { green, red, grey } from '@mui/material/colors';
import Typography from '@mui/material/Typography';

export const ChangeText = ({ value, suffix = '' }: { value: number; suffix?: string }) => {
  const color = value > 0 ? green[600] : value < 0 ? red[600] : grey[600];
  const prefix = value > 0 ? '+' : '';
  return (
    <Typography
      variant="body2"
      sx={{
        fontWeight: 'bold',
        color,
      }}
    >
      {prefix}
      {value.toFixed(2)}
      {suffix}
    </Typography>
  );
};

export const DirectionIcon = ({ direction }: { direction: string }) => {
  if (direction === '+') return <TrendingUpIcon sx={{ color: green[600], fontSize: 18 }} />;
  if (direction === '-') return <TrendingDownIcon sx={{ color: red[600], fontSize: 18 }} />;
  return <TrendingFlatIcon sx={{ color: grey[500], fontSize: 18 }} />;
};
