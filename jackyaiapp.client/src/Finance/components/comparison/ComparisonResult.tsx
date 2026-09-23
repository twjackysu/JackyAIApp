import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { getCategoryLabel } from '../../utils/financeHelpers';
import { CategoryScoreCard } from '../comprehensiveAnalysis/CategoryScoreCard';
import { StockIdentityHeader } from '../comprehensiveAnalysis/StockIdentityHeader';
import { CategoryIcon } from '../financeIcons';

import type { ComparisonSlot } from '../../hooks/useFinanceAnalysis';
import type { IndicatorCategory } from '@/apis/financeApis/types';

const CATEGORIES: IndicatorCategory[] = ['Technical', 'Chip', 'Fundamental'];

interface ComparisonResultProps {
  slots: ComparisonSlot[];
}

/**
 * Category-row comparison grid: one row per category present across the compared stocks, each
 * holding up to `slots.length` `CategoryScoreCard`s side by side (one column per stock) — lets a
 * reader scan "which stock scores best on Technical" horizontally instead of reading N full
 * single-stock reports. A slot whose fetch failed gets an inline error in its own column instead
 * of being silently dropped or blocking the other columns.
 */
export const ComparisonResult = ({ slots }: ComparisonResultProps) => {
  const activeCategories = CATEGORIES.filter((category) =>
    slots.some((slot) => slot.data?.scoring?.categoryScores.some((cs) => cs.category === category)),
  );
  const columnSize = { xs: 12, md: Math.floor(12 / slots.length) };

  return (
    <Stack spacing={3}>
      <Grid container spacing={2}>
        {slots.map((slot) => (
          <Grid key={slot.code} size={columnSize}>
            {slot.data ? (
              <Stack spacing={0.5}>
                <StockIdentityHeader data={slot.data} dense />
                {slot.data.scoring && (
                  <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 'bold' }}>
                    綜合評分: {Math.round(slot.data.scoring.overallScore)}
                  </Typography>
                )}
              </Stack>
            ) : (
              <Alert severity="error">{slot.error ?? `找不到 ${slot.code}`}</Alert>
            )}
          </Grid>
        ))}
      </Grid>
      {activeCategories.map((category) => (
        <Box key={category}>
          <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 1.5 }}>
            <CategoryIcon category={category} fontSize="small" />
            <Typography variant="h6" sx={{ fontWeight: 'bold' }}>
              {getCategoryLabel(category)}
            </Typography>
          </Stack>
          <Grid container spacing={2}>
            {slots.map((slot) => {
              const cs = slot.data?.scoring?.categoryScores.find((c) => c.category === category);
              return (
                <Grid key={slot.code} size={columnSize}>
                  {cs && <CategoryScoreCard cs={cs} />}
                </Grid>
              );
            })}
          </Grid>
        </Box>
      ))}
    </Stack>
  );
};

export default ComparisonResult;
