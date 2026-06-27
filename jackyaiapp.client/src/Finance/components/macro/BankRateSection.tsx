import PercentIcon from '@mui/icons-material/Percent';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import type { MacroEconomyData } from '@/apis/financeApis/types';

/**
 * Bank deposit / lending rates. (Spot FX is shown in the dashboard tiles, so it's
 * intentionally not duplicated here.)
 */
export const BankRateSection = ({ data }: { data: MacroEconomyData }) => {
  if (!data.bankRate) return null;
  const r = data.bankRate;

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 0.5 }}>
          <PercentIcon fontSize="small" color="action" />
          <Typography variant="subtitle2" color="text.secondary">
            銀行利率（{r.bankName}）
          </Typography>
        </Stack>
        <Stack spacing={0.3} sx={{ mt: 0.5 }}>
          <Stack direction="row" sx={{ justifyContent: 'space-between' }}>
            <Typography variant="body2">一年期定存(固定)</Typography>
            <Chip label={`${r.oneYearFixed?.toFixed(3) ?? '-'}%`} size="small" variant="outlined" />
          </Stack>
          <Stack direction="row" sx={{ justifyContent: 'space-between' }}>
            <Typography variant="body2">一年期定存(機動)</Typography>
            <Chip
              label={`${r.oneYearFloating?.toFixed(3) ?? '-'}%`}
              size="small"
              variant="outlined"
            />
          </Stack>
          {r.baseLendingRate && (
            <Stack direction="row" sx={{ justifyContent: 'space-between' }}>
              <Typography variant="body2">基準利率</Typography>
              <Chip
                label={`${r.baseLendingRate.toFixed(3)}%`}
                size="small"
                color="primary"
                variant="outlined"
              />
            </Stack>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
};
