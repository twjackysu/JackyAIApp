import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { getCategoryLabel } from '../../utils/financeHelpers';
import { CategoryScoreCard } from '../comprehensiveAnalysis/CategoryScoreCard';
import { StockIdentitySummary } from '../comprehensiveAnalysis/StockIdentitySummary';

import type { ComparisonSlot } from '../../hooks/useFinanceAnalysis';
import type { IndicatorCategory } from '@/apis/financeApis/types';

const CATEGORIES: IndicatorCategory[] = ['Technical', 'Chip', 'Fundamental'];

interface Props {
  slots: ComparisonSlot[];
}

/**
 * Category-row comparison layout (TASK-014 / ARCHITECTURE.md "Multi-stock/sector peer
 * comparison"): one row per category present across the compared stocks, each holding up
 * to 3 unmodified `CategoryScoreCard`s side by side, plus a per-stock identity header so
 * the reader knows which column is which. A slot whose fetch failed shows its error inline
 * in its own column instead of hiding that stock or blocking the others.
 */
export const ComparisonResult = ({ slots }: Props) => {
  const activeCategories = CATEGORIES.filter((category) =>
    slots.some((slot) => slot.data?.scoring?.categoryScores.some((cs) => cs.category === category)),
  );
  const columnSize = { xs: 12, md: Math.floor(12 / Math.max(slots.length, 1)) };

  return (
    <Box>
      <Grid container spacing={2} sx={{ mb: 3 }}>
        {slots.map((slot) => (
          <Grid key={slot.code} size={columnSize}>
            {slot.status === 'loading' && (
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                <CircularProgress size={18} />
                <Typography variant="body2" color="text.secondary">
                  {slot.code} 分析中...
                </Typography>
              </Stack>
            )}
            {slot.status === 'error' && (
              <Alert severity="error">{`找不到 ${slot.code}：${slot.errorMessage}`}</Alert>
            )}
            {slot.status === 'success' && slot.data && (
              <Stack spacing={0.5}>
                <StockIdentitySummary
                  market={slot.data.market}
                  companyName={slot.data.companyName}
                  stockCode={slot.data.stockCode}
                  direction={slot.data.scoring?.overallDirection}
                  variant="h6"
                />
                {slot.data.scoring && (
                  <Typography variant="body2" color="text.secondary">
                    綜合評分: {Math.round(slot.data.scoring.overallScore)}
                  </Typography>
                )}
              </Stack>
            )}
          </Grid>
        ))}
      </Grid>
      {activeCategories.map((category) => (
        <Box key={category} sx={{ mb: 3 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 'bold', mb: 1.5 }}>
            {getCategoryLabel(category)}
          </Typography>
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
    </Box>
  );
};
