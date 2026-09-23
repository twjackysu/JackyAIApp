import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { getDirectionLabel, getDirectionChipColor } from '../../utils/financeHelpers';
import { DirectionIcon } from '../financeIcons';

import type { StockAnalysisResultData } from '@/apis/financeApis/types';

interface StockIdentityHeaderProps {
  data: StockAnalysisResultData;
  /** Compact variant for narrow layouts (e.g. one column of a 3-wide comparison grid). */
  dense?: boolean;
}

/**
 * Market chip + company name/code + direction chip — the identity fragment shared by
 * `OverallScoreSection`'s full single-stock header and the comparison tab's per-stock header.
 */
export const StockIdentityHeader = ({ data, dense = false }: StockIdentityHeaderProps) => {
  const { scoring } = data;
  return (
    <Stack direction="row" spacing={dense ? 1 : 2} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
      <Chip
        label={data.market === 'US' ? 'US' : 'TW'}
        size="small"
        variant="outlined"
        sx={{ fontWeight: 'bold' }}
      />
      <Typography variant={dense ? 'subtitle1' : 'h5'} sx={{ fontWeight: 'bold' }}>
        {data.companyName} ({data.stockCode})
      </Typography>
      {scoring && (
        <Chip
          icon={<DirectionIcon direction={scoring.overallDirection} />}
          label={getDirectionLabel(scoring.overallDirection)}
          color={getDirectionChipColor(scoring.overallDirection)}
          variant="filled"
          size={dense ? 'small' : 'medium'}
          sx={{ fontWeight: 'bold', fontSize: dense ? '0.75rem' : '0.9rem' }}
        />
      )}
    </Stack>
  );
};

export default StockIdentityHeader;
