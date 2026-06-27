import AccountBalanceIcon from '@mui/icons-material/AccountBalance';
import AssessmentIcon from '@mui/icons-material/Assessment';
import DescriptionIcon from '@mui/icons-material/Description';
import KeyboardDoubleArrowDownIcon from '@mui/icons-material/KeyboardDoubleArrowDown';
import KeyboardDoubleArrowUpIcon from '@mui/icons-material/KeyboardDoubleArrowUp';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingFlatIcon from '@mui/icons-material/TrendingFlat';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';

import type { IndicatorCategory, SignalDirection } from '@/apis/financeApis/types';
import type { SvgIconProps } from '@mui/material/SvgIcon';

/** MUI icon for a signal direction (replaces the old emoji mapping). */
export const DirectionIcon = ({
  direction,
  ...props
}: { direction: SignalDirection } & SvgIconProps) => {
  switch (direction) {
    case 'StrongBullish':
      return <KeyboardDoubleArrowUpIcon {...props} />;
    case 'Bullish':
      return <TrendingUpIcon {...props} />;
    case 'Bearish':
      return <TrendingDownIcon {...props} />;
    case 'StrongBearish':
      return <KeyboardDoubleArrowDownIcon {...props} />;
    default:
      return <TrendingFlatIcon {...props} />;
  }
};

/** MUI icon for an indicator category (replaces the old emoji mapping). */
export const CategoryIcon = ({
  category,
  ...props
}: { category: IndicatorCategory } & SvgIconProps) => {
  switch (category) {
    case 'Chip':
      return <AccountBalanceIcon {...props} />;
    case 'Fundamental':
      return <DescriptionIcon {...props} />;
    case 'Technical':
    default:
      return <AssessmentIcon {...props} />;
  }
};
