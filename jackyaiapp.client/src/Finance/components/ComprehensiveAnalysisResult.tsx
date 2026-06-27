import ListAltIcon from '@mui/icons-material/ListAlt';
import Box from '@mui/material/Box';
import Divider from '@mui/material/Divider';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { CategoryScoreCard } from './comprehensiveAnalysis/CategoryScoreCard';
import { IndicatorTable } from './comprehensiveAnalysis/IndicatorTable';
import { OverallScoreSection } from './comprehensiveAnalysis/OverallScoreSection';
import { RiskSection } from './comprehensiveAnalysis/RiskSection';

import type { StockAnalysisResultData, IndicatorCategory } from '@/apis/financeApis/types';

interface Props {
  data: StockAnalysisResultData;
}

export const ComprehensiveAnalysisResult = ({ data }: Props) => {
  const categories: IndicatorCategory[] = ['Technical', 'Chip', 'Fundamental'];
  const active = categories.filter((c) => data.indicators.some((i) => i.category === c));
  return (
    <Box>
      <OverallScoreSection data={data} />
      {data.scoring && data.scoring.categoryScores.length > 0 && (
        <Grid container spacing={3} sx={{ mb: 3 }}>
          {data.scoring.categoryScores.map((cs) => (
            <Grid
              key={cs.category}
              size={{
                xs: 12,
                md: 4,
              }}
            >
              <CategoryScoreCard cs={cs} />
            </Grid>
          ))}
        </Grid>
      )}
      <RiskSection data={data} />
      <Divider sx={{ my: 3 }} />
      <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 2 }}>
        <ListAltIcon fontSize="small" />
        <Typography variant="h6" sx={{ fontWeight: 'bold' }}>
          指標明細
        </Typography>
      </Stack>
      <Stack spacing={1}>
        {active.map((c) => (
          <IndicatorTable key={c} indicators={data.indicators} category={c} />
        ))}
      </Stack>
    </Box>
  );
};

export default ComprehensiveAnalysisResult;
