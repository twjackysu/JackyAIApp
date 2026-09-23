import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { getDirectionLabel, getDirectionChipColor } from '../../utils/financeHelpers';
import { DirectionIcon } from '../financeIcons';

import type { MarketRegion, SignalDirection } from '@/apis/financeApis/types';

interface Props {
  market: MarketRegion;
  companyName: string;
  stockCode: string;
  /** Omitted when the caller has no scoring to show a direction chip for. */
  direction?: SignalDirection;
  variant?: 'h5' | 'h6';
}

/**
 * Market chip + company name/code + (optional) direction chip — the identity fragment
 * `OverallScoreSection` and the multi-stock comparison header both need, without the score
 * gauge or recommendation text that only makes sense in a single-stock layout.
 */
export const StockIdentitySummary = ({
  market,
  companyName,
  stockCode,
  direction,
  variant = 'h5',
}: Props) => (
  <Stack direction="row" spacing={2} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
    <Chip
      label={market === 'US' ? 'US' : 'TW'}
      size="small"
      variant="outlined"
      sx={{ fontWeight: 'bold' }}
    />
    <Typography variant={variant} sx={{ fontWeight: 'bold' }}>
      {companyName} ({stockCode})
    </Typography>
    {direction && (
      <Chip
        icon={<DirectionIcon direction={direction} />}
        label={getDirectionLabel(direction)}
        color={getDirectionChipColor(direction)}
        variant="filled"
        sx={{ fontWeight: 'bold', fontSize: '0.9rem' }}
      />
    )}
  </Stack>
);
