import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import HelpOutlineIcon from '@mui/icons-material/HelpOutline';

interface TrendIconProps {
  trend: string;
}

/**
 * Icon component for displaying trend direction
 */
export const TrendIcon = ({ trend }: TrendIconProps) => {
  switch (trend) {
    case 'bullish':
      return <ArrowUpwardIcon />;
    case 'bearish':
      return <ArrowDownwardIcon />;
    default:
      return <HelpOutlineIcon />;
  }
};

export default TrendIcon;
